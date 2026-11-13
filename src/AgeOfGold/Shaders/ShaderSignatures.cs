using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

internal sealed class ShaderSignatures
{
    internal string[] Lines { get; private set; }
    internal string Entry { get; }
    private readonly Dictionary<string, int> nodes = [];
    private readonly string[] tables;
    private int next;

    internal ShaderSignatures(string module, string stage = "vs")
    {
        // lazy: bounded SM6.0 graphics text; revisit when a supported family exceeds this size.
        if (module.Length > 4 * 1024 * 1024) throw new InvalidDataException("Shader module exceeds the signature adapter limit");
        Lines = module.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < Lines.Length; i++)
        {
            var match = Regex.Match(Lines[i], @"^!([\w.]+) = (.+)$");
            if (match.Success && !nodes.TryAdd(match.Groups[1].Value, i))
                throw new InvalidDataException("Duplicate shader metadata node");
        }
        var model = Read(Reference(One(Read("dx.shaderModel"))));
        if (stage is not ("vs" or "ps") || !model.SequenceEqual(new[] { "!\"" + stage + "\"", "i32 6", "i32 0" }))
            throw new InvalidDataException("Expected a shader-model 6.0 " + (stage == "ps" ? "pixel" : "vertex") + " program");
        var entry = Read(Reference(One(Read("dx.entryPoints"))));
        if (entry.Length != 5 || !Regex.IsMatch(entry[0], @"^void \(\)\* @[\w.$-]+$"))
            throw new InvalidDataException("Unsupported vertex entry metadata");
        Entry = entry[0].Split('@')[1];
        var signatures = Read(Reference(entry[2]));
        if (signatures.Length != 3 || signatures[2] != "null")
            throw new InvalidDataException("Unsupported vertex signature tables");
        tables = signatures.Take(2).Select(Reference).ToArray();
        next = checked(nodes.Keys.Where(k => int.TryParse(k, out _)).Select(int.Parse).Max() + 1);
        foreach (bool output in new[] { false, true })
        {
            var members = Members(output);
            if (members.Select(m => m.Id).Distinct().Count() != members.Length
                || members.Select(m => (m.Name.ToUpperInvariant(), m.Index)).Distinct().Count() != members.Length)
                throw new InvalidDataException("Ambiguous shader signature members");
        }
    }

    internal sealed record Member(string Node, int Id, string Name, int Index, int ComponentType, int Rows, int Columns, int Row, string[] Fields);

    internal Member Require(bool output, string name, int index, int componentType)
    {
        var matches = Members(output).Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && m.Index == index).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Missing or ambiguous shader semantic " + name + index);
        var member = matches[0];
        if (member.ComponentType != componentType || member.Rows != 1 || member.Columns != 4
            || member.Fields[3] != "i8 0" || member.Fields[5] != (output ? "i8 2" : "i8 0")
            || member.Row < 0 || member.Fields[9] != "i8 0")
            throw new InvalidDataException("Unsupported tangent signature encoding");
        return member;
    }

    internal Member Find(bool output, string name, int index)
    {
        var matches = Members(output).Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && m.Index == index).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Missing or ambiguous shader semantic " + name + index);
    }

    internal Member Output(int id) => Members(true).SingleOrDefault(m => m.Id == id) ?? throw new InvalidDataException("Missing shader output " + id);

    internal int Column(Member member) => Integer(member.Fields[9], "i8");

    /// <summary>Adds an input that receives another stage's output: same semantic, type, interpolation and register.</summary>
    internal int Link(Member produced)
    {
        var members = Members(false);
        if (members.Any(m => m.Name.Equals(produced.Name, StringComparison.OrdinalIgnoreCase) && m.Index == produced.Index))
            throw new InvalidDataException("Linked shader input already exists");
        int start = Integer(produced.Fields[9], "i8");
        if (members.Any(m => m.Row == produced.Row && m.Fields[3] == "i8 0"
                && Integer(m.Fields[9], "i8") < start + produced.Columns && start < Integer(m.Fields[9], "i8") + m.Columns))
            throw new InvalidDataException("Linked shader input register is occupied");
        int id = checked(members.Max(m => m.Id) + 1);
        var fields = produced.Fields.ToArray();
        fields[0] = "i32 " + id;
        fields[4] = "!" + Add(["i32 " + produced.Index]);
        fields[10] = "null";
        string node = Add(fields);
        Set(tables[0], [.. Read(tables[0]), "!" + node]);
        return id;
    }

    /// <summary>Records which components of an input the program reads, in addition to those already recorded.</summary>
    internal void Use(bool output, int id, int mask)
    {
        var member = Members(output).Single(m => m.Id == id);
        int previous = 0;
        if (member.Fields[10] != "null")
        {
            var properties = Read(Reference(member.Fields[10]));
            // lazy: only the usage-mask property is understood; other signature properties need their own handling.
            if (properties.Length != 2 || properties[0] != "i32 3") throw new InvalidDataException("Unsupported shader signature properties");
            previous = Integer(properties[1], "i32");
        }
        var fields = member.Fields.ToArray();
        fields[10] = "!" + Add(["i32 3", "i32 " + (previous | mask)]);
        Set(member.Node, fields);
    }

    /// <summary>The first register after every existing member of a signature.</summary>
    internal int FreeRow(bool output) => Members(output).Max(m => m.Row + m.Rows);

    internal int Append(bool output, Member template, int index, int? componentType = null, int? register = null)
    {
        var members = Members(output);
        if (members.Any(m => m.Name.Equals(template.Name, StringComparison.OrdinalIgnoreCase) && m.Index == index))
            throw new InvalidDataException("Secondary tangent signature already exists");
        int id = checked(members.Max(m => m.Id) + 1);
        int row = register ?? FreeRow(output);
        if (row < FreeRow(output)) throw new InvalidDataException("Requested shader signature register is occupied");
        if (row >= 32) throw new InvalidDataException("No free shader signature register");
        var fields = template.Fields.ToArray();
        fields[0] = "i32 " + id;
        if (componentType is { } component) fields[2] = "i8 " + component;
        fields[4] = "!" + Add(["i32 " + index]);
        fields[8] = "i32 " + row;
        string node = Add(fields);
        string table = tables[output ? 1 : 0];
        Set(table, [.. Read(table), "!" + node]);
        return id;
    }

    internal Member[] Members(bool output) => Read(tables[output ? 1 : 0]).Select(field =>
    {
        string node = Reference(field);
        var member = Read(node);
        if (member.Length != 11 || !Regex.IsMatch(member[1], "^!\"[A-Za-z_][A-Za-z_0-9]*\"$"))
            throw new InvalidDataException("Unsupported shader signature element");
        // lazy: single-row semantics; expand when a supported family requires matrix/array inputs.
        int index = Integer(One(Read(Reference(member[4]))), "i32");
        return new Member(node, Integer(member[0], "i32"), member[1][2..^1], index,
            Integer(member[2], "i8"), Integer(member[6], "i32"), Integer(member[7], "i8"), Integer(member[8], "i32"), member);
    }).ToArray();

    private string[] Read(string node)
    {
        if (!nodes.TryGetValue(node, out int line)) throw new InvalidDataException("Missing shader metadata node");
        string text = Lines[line];
        if (!text.Contains(" = !{", StringComparison.Ordinal) || !text.EndsWith('}'))
            throw new InvalidDataException("Expected a signature metadata tuple");
        text = text[(text.IndexOf("!{", StringComparison.Ordinal) + 2)..^1];
        // Selected signature nodes contain only scalars, references and the entry's void type.
        if (text.Contains('[') || text.Contains('{')) throw new InvalidDataException("Unsupported nested signature metadata");
        return text.Length == 0 ? [] : text.Split(',').Select(s => s.Trim()).ToArray();
    }

    private void Set(string node, string[] fields) => Lines[nodes[node]] = $"!{node} = !{{{string.Join(", ", fields)}}}";
    private string Add(string[] fields)
    {
        string node = (next++).ToString(CultureInfo.InvariantCulture);
        nodes.Add(node, Lines.Length);
        Lines = [.. Lines, ""];
        Set(node, fields);
        return node;
    }
    private static string One(string[] fields) => fields.Length == 1 ? fields[0] : throw new InvalidDataException("Ambiguous shader metadata reference");
    private static string Reference(string field) => Regex.IsMatch(field, @"^!\d+$") ? field[1..] : throw new InvalidDataException("Expected shader metadata reference");
    private static int Integer(string field, string type) => field.StartsWith(type + " ", StringComparison.Ordinal)
        && int.TryParse(field[(type.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
        ? value : throw new InvalidDataException("Invalid shader signature integer");
}
