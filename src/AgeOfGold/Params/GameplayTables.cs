using System.Buffers.Binary;
using System.Numerics;
using SoulsFormats;

namespace AgeOfGold.Params;

internal static class GameplayTables
{
    internal sealed record Destination(int RowId, string Name, uint Entity, uint UnlockFlag,
        byte Area, byte Block, byte Region, Vector3 Position, int TextId);

    internal static byte[] ReplaceDestinations(byte[] table, uint templateEntity, Destination destination)
    {
        var rows = Read(table, "BONFIRE_WARP_PARAM_ST", 6, 236);
        uint Value(PARAM.Row row, int at) => BinaryPrimitives.ReadUInt32LittleEndian(Payload(row).AsSpan(at));
        if (destination.Entity is 0 or uint.MaxValue || destination.UnlockFlag is 0 or uint.MaxValue
            || destination.TextId <= 0 || !float.IsFinite(destination.Position.X)
            || !float.IsFinite(destination.Position.Y) || !float.IsFinite(destination.Position.Z)
            || rows.Rows.Any(r => Value(r, 8) == destination.Entity || Value(r, 4) == destination.UnlockFlag))
            throw new InvalidDataException("Resting destination has invalid placement or occupied entity/flag references");
        var templates = rows.Rows.Where(r => Value(r, 8) == templateEntity).ToArray();
        if (templates.Length != 1) throw new InvalidDataException($"Expected one resting template for entity {templateEntity}; found {templates.Length}");
        var template = templates[0];
        var placed = Clone(rows, template, destination.RowId, destination.Name);
        var data = Payload(placed);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), destination.UnlockFlag);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), destination.Entity);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), -1);
        data[32] = destination.Area; data[33] = destination.Block; data[34] = destination.Region;
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(36), destination.Position.X);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(40), destination.Position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(44), destination.Position.Z);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(48), destination.TextId);
        rows.Rows = [placed];
        return rows.Write();
    }

    internal static byte[] AddInteraction(byte[] table, int templateRow, int rowId, string name, int promptId)
    {
        var rows = Read(table, "ACTIONBUTTON_PARAM_ST", 2, 100);
        if (promptId <= 0) throw new InvalidDataException("An interaction requires a prompt reference");
        var template = rows.Rows.SingleOrDefault(r => r.ID == templateRow)
            ?? throw new InvalidDataException($"Interaction template row {templateRow} is absent");
        var added = Clone(rows, template, rowId, name);
        BinaryPrimitives.WriteInt32LittleEndian(Payload(added).AsSpan(52), promptId);
        Insert(rows, added);
        return rows.Write();
    }

    /// <summary>Adds a character row that is an installed row with another body size (how tall and how wide the character stands) or display mark.</summary>
    /// <param name="mark">The one display mark, of the row's first sixteen, whose meshes the row draws; null keeps the template's marks.</param>
    /// <param name="standingRow">An installed row whose standing toward the player the new row takes: with a friendly character's, it is left alone.</param>
    /// <param name="variation">The behaviour variation whose rows answer the character's attack events; null keeps the template's.</param>
    internal static byte[] AddCharacter(byte[] table, int templateRow, int rowId, string name, (float Height, float Radius)? body, int? mark = null, int? standingRow = null, int? variation = null)
    {
        if (mark is < 0 or > 15) throw new InvalidDataException("A character row shows one of its first sixteen display marks");
        var rows = Read(table, "NPC_PARAM_ST", 9, 736);
        if (body is { } size && (!(size.Height > 0) || !(size.Radius > 0) || !float.IsFinite(size.Height) || !float.IsFinite(size.Radius))) throw new InvalidDataException("A character needs a body height and radius");
        var template = rows.Rows.SingleOrDefault(r => r.ID == templateRow) ?? throw new InvalidDataException($"Character template row {templateRow} is absent");
        var added = Clone(rows, template, rowId, name);
        // The row holds the body size twice: against the map (and for finding routes), and against other characters.
        // Without a size the template's own is kept.
        if (body is { } given)
            foreach (int at in new[] { 20, 340 })
            {
                BinaryPrimitives.WriteSingleLittleEndian(Payload(added).AsSpan(at), given.Height); BinaryPrimitives.WriteSingleLittleEndian(Payload(added).AsSpan(at + 4), given.Radius);
            }
        if (standingRow is { } standing)
        {
            var friend = rows.Rows.SingleOrDefault(r => r.ID == standing) ?? throw new InvalidDataException($"Character standing row {standing} is absent");
            // Two bytes tell the host what kind of character this is and whose side it is on.
            foreach (int at in new[] { 304, 307 }) Payload(added)[at] = Payload(friend)[at];
        }
        // Sixteen bits, one for each of the marks a model's mesh names can open with ("#00#" to "#15#").
        if (mark is { } shown) BinaryPrimitives.WriteUInt16LittleEndian(Payload(added).AsSpan(330), (ushort)(1 << shown));
        // The row opens with four bytes of the host's own; the behaviour variation whose rows answer its attacks follows them.
        if (variation is <= 0) throw new InvalidDataException("A character's behaviour variation is a positive number");
        if (variation is { } answers) BinaryPrimitives.WriteInt32LittleEndian(Payload(added).AsSpan(4), answers);
        Insert(rows, added);
        return rows.Write();
    }

    // What a think row says that means the same in both games, by where each keeps it: the source's place, the host's, and
    // how it is stored (a whole number of four bytes, of two, or of one; a fraction; or seconds, whole in the source and a
    // fraction in the host). In order: the battle script; how long it pushes at a wall before giving up; how far it sees;
    // where sound stops reaching it and how far it smells; how far from home it goes, turns back and fights; how long it
    // idles and how long and how far it looks back at a target as it goes home; when it forgets what it saw and heard;
    // how near a target starts a battle; how it weighs joining an attack; and how wide and how tall its sight is.
    // lazy: the rest stays the template's: calling for help, the goals it takes on noticing something, and hearing, which
    // the host keeps by other means. That a forgetting time is seconds in both games is
    // taken from the two tables' usual values, not seen in play.
    private static readonly (int Source, int Host, char Kind)[] SharedThinking =
    [
        (4, 8, 'i'), (24, 28, 'f'), (48, 52, 'w'), (52, 56, 'w'), (54, 58, 'w'), (56, 60, 'w'), (58, 62, 'w'), (60, 64, 'w'), (62, 66, 'w'), (64, 68, 'w'), (66, 70, 'w'),
        (68, 32, 's'), (70, 72, 's'), (72, 76, 'w'), (80, 84, 'b'), (81, 85, 'b'), (82, 86, 'b'),
    ];

    /// <summary>
    /// Adds a think row that is an installed row with what a source think row says of the same things: the battle script
    /// above all (the host finds an enemy's battle script, and the goal it runs in battle, by that number), then how far
    /// and how widely it sees, how far it strays from home, and how long it remembers.
    /// </summary>
    /// <param name="logic">The number of what the enemy does out of battle, as the host numbers it.</param>
    internal static byte[] AddThinking(byte[] table, int templateRow, int rowId, string name, byte[] source, int logic)
    {
        if (source.Length != 112) throw new InvalidDataException("Source think row is not of the known layout");
        if (logic <= 0) throw new InvalidDataException("A think row names what its enemy does out of battle by a positive number");
        if (BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(4)) <= 0) throw new InvalidDataException("A think row names a battle script by a positive number");
        var rows = Read(table, "NPC_THINK_PARAM_ST", 2, 228);
        var template = rows.Rows.SingleOrDefault(r => r.ID == templateRow) ?? throw new InvalidDataException($"Think template row {templateRow} is absent");
        var added = Clone(rows, template, rowId, name);
        // The row opens with four bytes of the host's own; the out-of-battle number follows.
        BinaryPrimitives.WriteInt32LittleEndian(Payload(added).AsSpan(4), logic);
        foreach (var (from, to, kind) in SharedThinking)
            switch (kind)
            {
                case 'i' or 'f': source.AsSpan(from, 4).CopyTo(Payload(added).AsSpan(to)); break;
                case 'w': source.AsSpan(from, 2).CopyTo(Payload(added).AsSpan(to)); break;
                case 'b': Payload(added)[to] = source[from]; break;
                case 's': BinaryPrimitives.WriteSingleLittleEndian(Payload(added).AsSpan(to), BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(from))); break;
            }
        Insert(rows, added);
        return rows.Write();
    }

    /// <summary>The battle number an installed think row names.</summary>
    internal static int BattleOf(byte[] table, int row) =>
        BinaryPrimitives.ReadInt32LittleEndian(Payload(Read(table, "NPC_THINK_PARAM_ST", 2, 228).Rows.SingleOrDefault(r => r.ID == row) ?? throw new InvalidDataException($"Think row {row} is absent")).AsSpan(8));

    /// <summary>
    /// Adds a source character's behaviour rows as they are: both games lay a behaviour row out alike. A row answers an
    /// attack event's request for its character's variation by naming an attack row.
    /// </summary>
    internal static byte[] AddBehaviours(byte[] table, IReadOnlyList<(int Id, byte[] Row)> rows)
    {
        var host = Read(table, "BEHAVIOR_PARAM_ST", 2, 32);
        // A variation the host already answers would mix another character's attacks with these.
        var variations = host.Rows.Select(r => BinaryPrimitives.ReadInt32LittleEndian(Payload(r))).ToHashSet();
        foreach (var (id, row) in rows)
        {
            if (row.Length != 32) throw new InvalidDataException($"Source behaviour row {id} is not laid out as the host's");
            if (variations.Contains(BinaryPrimitives.ReadInt32LittleEndian(row))) throw new InvalidDataException($"The host already answers the behaviour variation of source row {id}");
            var added = Clone(host, host.Rows[0], id, "aog_behaviour");
            row.CopyTo(Payload(added), 0);
            Insert(host, added);
        }
        return host.Write();
    }

    /// <summary>
    /// Adds attack rows that are an installed row of a like attack under a source row's values. The start of an attack
    /// row means the same in both games; of it, what describes the hit itself is taken from the source.
    /// </summary>
    internal static byte[] AddAttacks(byte[] table, int templateRow, IReadOnlyList<(int Id, byte[] Row)> rows)
    {
        var host = Read(table, "ATK_PARAM_ST", 4, 456);
        var template = host.Rows.SingleOrDefault(r => r.ID == templateRow) ?? throw new InvalidDataException($"Attack template row {templateRow} is absent");
        foreach (var (id, row) in rows)
        {
            if (row.Length != 128) throw new InvalidDataException($"Source attack row {id} is not of the known layout");
            var added = Clone(host, template, id, "aog_attack");
            // lazy: hit sizes and reach, the attachment points each hit spans, and the damage values. The effects an attack
            // applies, its guard, stagger and material values stay the template's until each is matched between the games.
            foreach (var (from, length) in new[] { (0, 24), (44, 16), (80, 12) }) row.AsSpan(from, length).CopyTo(Payload(added).AsSpan(from));
            Insert(host, added);
        }
        return host.Write();
    }

    /// <summary>Clears the effect a host visual-effect row plays, so whatever applies the row shows nothing.</summary>
    internal static byte[] SilenceEffect(byte[] table, int rowId)
    {
        var rows = Read(table, "SP_EFFECT_VFX_PARAM_ST", 1, 164);
        var row = rows.Rows.SingleOrDefault(r => r.ID == rowId) ?? throw new InvalidDataException($"Host visual-effect row {rowId} is absent");
        // The row begins with the effect it plays for as long as it applies.
        if (BinaryPrimitives.ReadInt32LittleEndian(Payload(row)) <= 0) throw new InvalidDataException($"Host visual-effect row {rowId} plays no effect");
        BinaryPrimitives.WriteInt32LittleEndian(Payload(row), -1);
        return rows.Write();
    }

    private static PARAM Read(byte[] bytes, string type, short version, int size)
    {
        var table = PARAM.Read(bytes);
        // lazy: only the observed revisions; classify another host layout before enabling it.
        if (table.ParamType != type || table.ParamdefDataVersion != version || table.DetectedSize != size
            || table.BigEndian || (byte)table.Format2D != 0x85 || (byte)table.Format2E != 7
            || table.ParamdefFormatVersion != 0 || table.UnnamedRows || table.HeaderlessRows
            || table.Rows.Count == 0 || table.Rows.Select(r => r.ID).Distinct().Count() != table.Rows.Count)
            throw new InvalidDataException($"Unsupported {type} layout: {table.ParamType}, revision {table.ParamdefDataVersion}, row size {table.DetectedSize}");
        var definition = new PARAMDEF { ParamType = type, DataVersion = version };
        definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
        table.ApplyParamdef(definition);
        if (!table.Write().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException($"Unsupported {type} encoding: unchanged rows do not round trip exactly");
        return table;
    }

    // The host's tables list their rows in ascending identity; a new row goes where its identity belongs.
    private static void Insert(PARAM table, PARAM.Row row)
    {
        int at = table.Rows.FindIndex(r => r.ID > row.ID);
        table.Rows.Insert(at < 0 ? table.Rows.Count : at, row);
    }

    private static PARAM.Row Clone(PARAM table, PARAM.Row template, int id, string name)
    {
        if (id <= 0 || table.Rows.Any(r => r.ID == id) || string.IsNullOrWhiteSpace(name) || name.Contains('\0'))
            throw new InvalidDataException("New gameplay row identity is invalid or occupied");
        var row = new PARAM.Row(template) { ID = id, Name = name };
        // The library's cell clone shares array values; an edit must never mutate the template row.
        row["raw"].Value = Payload(template).ToArray();
        return row;
    }

    private static byte[] Payload(PARAM.Row row) => (byte[])row["raw"].Value;
}
