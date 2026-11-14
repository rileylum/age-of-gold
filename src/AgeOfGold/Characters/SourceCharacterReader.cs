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
}
