using System.Buffers.Binary;
using SoulsFormats;

namespace AgeOfGold.Thinking;

/// <summary>Reads how the source's enemies think: the battle script each think row names, and the compiled scripts themselves.</summary>
internal static class SourceThinking
{
    // The source's think table by the row length this reader knows: a row opens with the number of what its enemy does
    // out of battle, then its battle number.
    private const int RowSize = 112, BattleAt = 4;

    /// <summary>The source's think rows as stored, by row.</summary>
    internal static SortedDictionary<int, byte[]> Rows(Install source)
    {
        var bundle = BND3.Read(source.Read("param/GameParam/GameParam.parambnd.dcx"));
        var file = bundle.Files.SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith("/NpcThinkParam.param", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Source install has no think table");
        var table = PARAM.Read(file.Bytes);
        if (table.ParamType != "NPC_THINK_PARAM_ST" || table.DetectedSize != RowSize || table.BigEndian) throw new InvalidDataException($"Unsupported source think table: {table.ParamType}, row size {table.DetectedSize}");
        var definition = new PARAMDEF { ParamType = table.ParamType, DataVersion = table.ParamdefDataVersion };
        definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = RowSize });
        table.ApplyParamdef(definition);
        var rows = new SortedDictionary<int, byte[]>();
        foreach (var row in table.Rows)
            if (!rows.TryAdd(row.ID, (byte[])row["raw"].Value)) throw new InvalidDataException($"Source think row {row.ID} appears twice");
        return rows;
    }

    /// <summary>The number of what a source think row's enemy does out of battle.</summary>
    internal static int Logic(byte[] row) => BinaryPrimitives.ReadInt32LittleEndian(row);

    /// <summary>The battle number a source think row names; nought when it names none.</summary>
    internal static int Battle(byte[] row) => BinaryPrimitives.ReadInt32LittleEndian(row.AsSpan(BattleAt));

    private static string Member(BinderFile file) => file.Name.Replace('\\', '/').Split('/')[^1];

    /// <summary>The compiled battle script of one number, from the binder of a map's scripts.</summary>
    internal static byte[] BattleScript(Install source, string map, int battle) =>
        BND3.Read(source.Read($"script/{map}.luabnd.dcx")).Files.SingleOrDefault(f => Member(f).Equals($"{battle}_battle.lua", StringComparison.OrdinalIgnoreCase))?.Bytes
            ?? throw new InvalidDataException($"Source map {map} holds no battle script {battle}");

    /// <summary>The compiled scripts every source enemy's script may lean on, by name, in name order.</summary>
    internal static (string Name, byte[] Script)[] Shared(Install source) =>
        [.. BND3.Read(source.Read("script/aiCommon.luabnd.dcx")).Files.Where(f => Member(f).EndsWith(".lua", StringComparison.OrdinalIgnoreCase)).Select(f => (Member(f), f.Bytes)).OrderBy(f => f.Item1, StringComparer.Ordinal)];
}
