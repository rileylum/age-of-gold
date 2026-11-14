using System.Buffers.Binary;
using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>Reads what the source's character table says of a character's body.</summary>
internal static class SourceCharacterReader
{
    // The source's character table: the row length this reader knows, and where a row keeps how tall and how wide its character stands.
    private const int RowSize = 336, Height = 16, Radius = 20;

    /// <summary>The body size of each character row that has one: height and radius, in metres.</summary>
    internal static SortedDictionary<int, (float Height, float Radius)> BodySizes(Install source)
    {
        var bundle = BND3.Read(source.Read("param/GameParam/GameParam.parambnd.dcx"));
        var file = bundle.Files.SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith("/NpcParam.param", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Source install has no character table");
        var table = PARAM.Read(file.Bytes);
        if (table.ParamType != "NPC_PARAM_ST" || table.DetectedSize != RowSize || table.BigEndian || table.Rows.Count == 0)
            throw new InvalidDataException($"Unsupported source character table: {table.ParamType}, row size {table.DetectedSize}");
        var definition = new PARAMDEF { ParamType = table.ParamType, DataVersion = table.ParamdefDataVersion };
        definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = RowSize });
        table.ApplyParamdef(definition);
        var sizes = new SortedDictionary<int, (float, float)>();
        foreach (var row in table.Rows)
        {
            var raw = (byte[])row["raw"].Value;
            float height = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(Height)), radius = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(Radius));
            if (sizes.ContainsKey(row.ID)) throw new InvalidDataException($"Source character row {row.ID} appears twice");
            // The table also holds rows with no body, for things that are never stood in the world; those are left out.
            if (height > 0 && radius > 0 && float.IsFinite(height) && float.IsFinite(radius)) sizes.Add(row.ID, (height, radius));
        }
        return sizes;
    }

    /// <param name="Variation">The behaviour variation the character's row names.</param>
    /// <param name="Behaviours">The variation's behaviour rows: each answers one number an attack event may ask for.</param>
    /// <param name="Hits">The attack rows those behaviours name.</param>
    /// <param name="Projectiles">Behaviours that name a projectile instead of an attack row.</param>
    internal sealed record SourceAttacks(int Variation, (int Id, byte[] Row)[] Behaviours, (int Id, byte[] Row)[] Hits, int Projectiles);

    /// <summary>What answers a character's attack events: its behaviour rows and the attack rows they name.</summary>
    internal static SourceAttacks Attacks(Install source, int characterRow)
    {
        var bundle = BND3.Read(source.Read("param/GameParam/GameParam.parambnd.dcx"));
        SortedDictionary<int, byte[]> Rows(string name, string type, int size)
        {
            var file = bundle.Files.SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith("/" + name + ".param", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Source install has no table " + name);
            var table = PARAM.Read(file.Bytes);
            if (table.ParamType != type || table.DetectedSize != size || table.BigEndian || table.Rows.Count == 0) throw new InvalidDataException($"Unsupported source table {name}: {table.ParamType}, row size {table.DetectedSize}");
            var definition = new PARAMDEF { ParamType = table.ParamType, DataVersion = table.ParamdefDataVersion };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
            table.ApplyParamdef(definition);
            var rows = new SortedDictionary<int, byte[]>();
            foreach (var row in table.Rows) if (!rows.TryAdd(row.ID, (byte[])row["raw"].Value)) throw new InvalidDataException($"Source table {name} repeats row {row.ID}");
            return rows;
        }
        // A character row opens with its behaviour variation. A behaviour row holds its variation, the number an attack
        // event asks for, what kind of thing it names (an attack row, or a projectile) and that thing's row.
        if (!Rows("NpcParam", "NPC_PARAM_ST", RowSize).TryGetValue(characterRow, out var character)) throw new InvalidDataException($"Source character row {characterRow} is absent");
        int variation = BinaryPrimitives.ReadInt32LittleEndian(character);
        if (variation <= 0) throw new InvalidDataException($"Source character row {characterRow} names no behaviour variation");
        var behaviours = Rows("BehaviorParam", "BEHAVIOR_PARAM_ST", 32).Where(r => BinaryPrimitives.ReadInt32LittleEndian(r.Value) == variation).Select(r => (r.Key, r.Value)).ToArray();
        var attackRows = Rows("AtkParam_Npc", "ATK_PARAM_ST", 128);
        var hits = new SortedDictionary<int, byte[]>(); int projectiles = 0;
        foreach (var (id, row) in behaviours)
        {
            int named = BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(12));
            switch (row[9])
            {
                case 0: hits[named] = attackRows.TryGetValue(named, out var hit) ? hit : throw new InvalidDataException($"Source behaviour row {id} names a missing attack row {named}"); break;
                case 1: projectiles++; break;
                default: throw new InvalidDataException($"Source behaviour row {id} names an unclassified kind of thing");
            }
        }
        return new(variation, behaviours, hits.Select(h => (h.Key, h.Value)).ToArray(), projectiles);
    }
}
