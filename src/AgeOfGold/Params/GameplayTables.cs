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
        rows.Rows.Add(added);
        return rows.Write();
    }

    /// <summary>Adds a character row that is an installed row with another body size: how tall and how wide the character stands.</summary>
    /// <param name="mark">The one display mark, of the row's first sixteen, whose meshes the row draws; null keeps the template's marks.</param>
    internal static byte[] AddCharacter(byte[] table, int templateRow, int rowId, string name, float height, float radius, int? mark = null)
    {
        if (mark is < 0 or > 15) throw new InvalidDataException("A character row shows one of its first sixteen display marks");
        var rows = Read(table, "NPC_PARAM_ST", 9, 736);
        if (!(height > 0) || !(radius > 0) || !float.IsFinite(height) || !float.IsFinite(radius)) throw new InvalidDataException("A character needs a body height and radius");
        var template = rows.Rows.SingleOrDefault(r => r.ID == templateRow) ?? throw new InvalidDataException($"Character template row {templateRow} is absent");
        var added = Clone(rows, template, rowId, name);
        // The row holds the body size twice: against the map (and for finding routes), and against other characters.
        foreach (int at in new[] { 20, 340 })
        {
            BinaryPrimitives.WriteSingleLittleEndian(Payload(added).AsSpan(at), height); BinaryPrimitives.WriteSingleLittleEndian(Payload(added).AsSpan(at + 4), radius);
        }
        // Sixteen bits, one for each of the marks a model's mesh names can open with ("#00#" to "#15#").
        if (mark is { } shown) BinaryPrimitives.WriteUInt16LittleEndian(Payload(added).AsSpan(330), (ushort)(1 << shown));
        rows.Rows.Add(added);
        return rows.Write();
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
