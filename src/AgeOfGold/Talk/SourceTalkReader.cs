using System.Buffers.Binary;
using AgeOfGold.Text;
using SoulsFormats;

namespace AgeOfGold.Talk;

/// <summary>Reads what a source character says first.</summary>
internal static class SourceTalkReader
{
    // The source's talk table: the row length this reader knows; a row begins with the identity of its line of text.
    private const int RowSize = 32, Lines = 1000000;

    /// <summary>
    /// The opening line of a talk script, in the source's English text. A script names the talk rows it speaks; a
    /// character's rows share their leading digits and are numbered in the order the lines were written.
    /// </summary>
    // lazy: the lowest-numbered line of the script's commonest group stands for "what is said first"; the script's
    // own states and conditions are not followed, and only one line of a speech is taken.
    internal static string OpeningLine(Install source, string map, int talk)
    {
        var script = BND3.Read(source.Read($"script/talk/{map}.talkesdbnd.dcx")).Files.SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith($"/t{talk}.esd", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Source map {map} has no talk script {talk}");
        var bundle = BND3.Read(source.Read("param/GameParam/GameParam.parambnd.dcx"));
        var file = bundle.Files.SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith("/TalkParam.param", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Source install has no talk table");
        var table = PARAM.Read(file.Bytes);
        if (table.ParamType != "TALK_PARAM_ST" || table.DetectedSize != RowSize || table.BigEndian) throw new InvalidDataException($"Unsupported source talk table: {table.ParamType}, row size {table.DetectedSize}");
        var definition = new PARAMDEF { ParamType = table.ParamType, DataVersion = table.ParamdefDataVersion };
        definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = RowSize });
        table.ApplyParamdef(definition);
        var rows = table.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => (byte[])g.First()["raw"].Value);
        // A script holds the rows it speaks as plain four-byte numbers; any such number that is a row is taken as named.
        var named = new SortedSet<int>();
        for (int at = 0; at + 4 <= script.Bytes.Length; at++)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(script.Bytes.AsSpan(at));
            if (value >= Lines && rows.ContainsKey(value)) named.Add(value);
        }
        var groups = named.GroupBy(id => id / Lines).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToArray();
        if (groups.Length == 0 || groups.Length > 1 && groups[0].Count() == groups[1].Count()) throw new InvalidDataException($"Source talk script {talk} does not name one character's lines");
        int line = BinaryPrimitives.ReadInt32LittleEndian(rows[groups[0].Min()]);
        string text = MessageBundle.ReadSource(source.Read("msg/ENGLISH/menu.msgbnd.dcx"), "Conversation_.fmg", line);
        return string.IsNullOrWhiteSpace(text) ? throw new InvalidDataException($"Source talk line {line} has no text") : text;
    }
}
