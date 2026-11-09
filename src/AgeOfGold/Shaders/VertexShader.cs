using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

internal static class VertexShader
{
    internal static string AddLayerFrames(string module) => AddDirection(AddDirection(AddSecondaryTangent(module), 4, false), 6, false);

    internal static string AddSecondaryTangent(string module) => AddDirection(module, 2, true);

    private static string AddDirection(string module, int index, bool packed)
    {
        var signatures = new ShaderSignatures(module);
        var input = signatures.Require(false, "TANGENT", 0, 5);
        var output = signatures.Require(true, "TANGENT", 0, 9);
        int newInput = signatures.Append(false, input, index, packed ? 5 : 9);
        int newOutput = signatures.Append(true, output, index);
        var lines = signatures.Lines;
        int start = Array.FindIndex(lines, l => l == $"define void @{signatures.Entry}() {{");
        int end = Array.FindIndex(lines, start + 1, l => l == "}");
        if (start < 0 || end < 0 || lines.Count(l => l.StartsWith("define ")) != 1)
            throw new InvalidDataException("Expected one vertex entry function");
        var instructions = new Dictionary<string, Instruction>();
        for (int i = start + 1; i < end; i++)
        {
            var text = lines[i].Split(';')[0].Trim();
            var match = Regex.Match(text, @"^(%[\w.$-]+) = (.+)$");
            if (match.Success && !instructions.TryAdd(match.Groups[1].Value,
                    new(i, match.Groups[2].Value, Operands(match.Groups[2].Value))))
                throw new InvalidDataException("Duplicate vertex instruction result");
        }
        // lazy: one scalar SM6.0 entry; extend when another supported family needs other IR forms.
        if (instructions.Count > 16384 || lines.Length > 32768)
            throw new InvalidDataException("Vertex shader exceeds the bounded scalar adapter");
        var loads = instructions.Where(p => Load(p.Value.Text, input.Id)).Select(p => p.Key).ToHashSet();
        if (loads.Count == 0) throw new InvalidDataException("Primary tangent is not read");
        var users = new Dictionary<string, List<string>>();
        foreach (var (name, instruction) in instructions)
            foreach (var operand in instruction.Operands)
            {
                if (!users.TryGetValue(operand, out var list)) users.Add(operand, list = []);
                list.Add(name);
            }
        var roots = packed ? loads.ToDictionary(n => n, n => int.Parse(Regex.Match(instructions[n].Text, @"i8 ([0-3])").Groups[1].Value))
            : DecodeRoots(instructions, loads, users);
        var dependent = roots.Keys.ToHashSet();
        var pending = new Queue<string>(roots.Keys);
        while (pending.TryDequeue(out var value))
        {
            if (users.TryGetValue(value, out var list))
                foreach (var user in list)
                    if (dependent.Add(user)) pending.Enqueue(user);
        }

        var stores = new Dictionary<int, Match>();
        for (int i = start + 1; i < end; i++)
        {
            string text = lines[i].Split(';')[0].Trim();
            if ((text.StartsWith("br ") || text.StartsWith("switch ")) && Operands(text).Any(dependent.Contains))
                throw new InvalidDataException("Tangent-dependent control flow cannot be shared by the second tangent");
            var store = Store(text, output.Id);
            if (store.Success) stores.Add(i, store);
        }
        if (stores.Count == 0 || !stores.Values.Any(m => dependent.Contains(m.Groups["value"].Value))
            || !stores.Values.Select(m => m.Groups["column"].Value).ToHashSet().SetEquals(["0", "1", "2", "3"]))
            throw new InvalidDataException("Expected a complete tangent output derived from its input");

        var slice = new HashSet<string>();
        void Visit(string name)
        {
            if (!dependent.Contains(name) || !slice.Add(name)) return;
            var instruction = instructions[name];
            if (!roots.ContainsKey(name) && !Pure(instruction.Text))
                throw new InvalidDataException("Unsupported tangent transform instruction: " + instruction.Text.Split(' ')[0]);
            foreach (var operand in instruction.Operands) Visit(operand);
        }
        foreach (var store in stores.Values) Visit(store.Groups["value"].Value);
        if (!roots.Keys.All(slice.Contains)) throw new InvalidDataException("Tangent has unused or unrelated input components");
        if (!packed)
        {
            var visited = new HashSet<string>();
            void CheckDecode(string name)
            {
                if (roots.ContainsKey(name) || !visited.Add(name) || !instructions.TryGetValue(name, out var instruction)) return;
                if (loads.Contains(name)) throw new InvalidDataException("Tangent transform bypasses its decoded components");
                foreach (var operand in instruction.Operands) CheckDecode(operand);
            }
            foreach (var store in stores.Values) CheckDecode(store.Groups["value"].Value);
        }
        var names = instructions.Keys.Where(slice.Contains).Select((name, i) => (name, fresh: $"%aog_tangent{index}_" + i))
            .ToDictionary(p => p.name, p => p.fresh);
        if (names.Values.Any(instructions.ContainsKey)) throw new InvalidDataException("Secondary tangent names already exist");
        string Remap(string text) => Regex.Replace(text, @"%[\w.$-]+", m => names.GetValueOrDefault(m.Value, m.Value));
        var after = new Dictionary<int, string>();
        foreach (var (name, fresh) in names)
        {
            var instruction = instructions[name];
            string text = Remap(instruction.Text);
            if (!packed && roots.TryGetValue(name, out int component))
                text = $"call float @dx.op.loadInput.f32(i32 4, i32 {newInput}, i32 0, i8 {component}, i32 undef)";
            else if (loads.Contains(name))
                text = Regex.Replace(text, @"(@dx\.op\.loadInput\.i32\(i32 4, i32 )\d+", "${1}" + newInput);
            after.Add(instruction.Line, $"  {fresh} = {text}");
        }
        foreach (var (line, store) in stores)
            after.Add(line, $"  call void @dx.op.storeOutput.f32(i32 5, i32 {newOutput}, i32 0, i8 {store.Groups["column"].Value}, float {Remap(store.Groups["value"].Value)})");
        var result = string.Join('\n', lines.SelectMany((line, i) => after.TryGetValue(i, out var added) ? new[] { line, added } : [line]));
        if (!packed && !lines.Any(l => l.StartsWith("declare float @dx.op.loadInput.f32(")))
            result += "\ndeclare float @dx.op.loadInput.f32(i32, i32, i32, i8, i32)\n";
        return result;
    }

    private static Dictionary<string, int> DecodeRoots(Dictionary<string, Instruction> instructions, HashSet<string> loads,
        Dictionary<string, List<string>> users)
    {
        var roots = new Dictionary<string, int>();
        string[]? contract = null;
        foreach (string load in loads)
        {
            string current = load;
            var steps = new List<string>();
            while (users.TryGetValue(current, out var uses))
            {
                var unique = uses.Distinct().ToArray();
                if (unique.Length != 1) break;
                var next = instructions[unique[0]];
                string normalized = Regex.Replace(next.Text, @"%[\w.$-]+", m => m.Value == current ? "%component" : m.Value);
                bool conversion = steps.Count == 0 && normalized == "uitofp i32 %component to float";
                bool affine = steps.Count > 0 && Regex.IsMatch(normalized,
                    @"^f(add|sub|mul|div)(?: fast)? float (%component, ([-+0-9.eE]+|0x[0-9A-Fa-f]+)|([-+0-9.eE]+|0x[0-9A-Fa-f]+), %component)$");
                if (!conversion && !affine) break;
                steps.Add(normalized); current = unique[0];
                // lazy: scalar affine unpacking only; nonlinear or longer encodings need a separate proof.
                if (steps.Count > 8) throw new InvalidDataException("Unsupported tangent decoding chain");
            }
            bool scaled = steps.Count == 3 && steps.Skip(1).Count(s => s.StartsWith("fmul ")
                || Regex.IsMatch(s, @"^fdiv(?: fast)? float %component, ")) == 1;
            bool biased = steps.Count == 3 && steps.Skip(1).Count(s => s.StartsWith("fadd ") || s.StartsWith("fsub ")) == 1;
            if (!scaled || !biased || contract is not null && !steps.SequenceEqual(contract))
                throw new InvalidDataException("Expected one matching affine decode for all tangent components");
            contract = steps.ToArray();
            roots.Add(current, int.Parse(Regex.Match(instructions[load].Text, @"i8 ([0-3])").Groups[1].Value));
        }
        if (!roots.Values.ToHashSet().SetEquals([0, 1, 2, 3]))
            throw new InvalidDataException("Expected four independently decoded tangent components");
        return roots;
    }

    private sealed record Instruction(int Line, string Text, string[] Operands);
    private static string[] Operands(string text) => Regex.Matches(text, @"%[\w.$-]+").Select(m => m.Value).ToArray();
    private static bool Load(string text, int id) => Regex.IsMatch(text,
        @"^call i32 @dx\.op\.loadInput\.i32\(i32 4, i32 " + id + @", i32 0, i8 [0-3], i32 undef\)$");
    private static Match Store(string text, int id) => Regex.Match(text,
        @"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 " + id + @", i32 0, i8 (?<column>[0-3]), float (?<value>%[\w.$-]+|[-+0-9.eE]+|0x[0-9A-Fa-f]+)\)$");

    private static bool Pure(string text)
    {
        string opcode = text.Split(' ')[0];
        if (opcode is "fadd" or "fsub" or "fmul" or "fdiv" or "uitofp" or "sitofp"
            or "select" or "phi" or "fcmp" or "icmp" or "add" or "sub" or "mul"
            or "and" or "or" or "xor" or "shl" or "lshr" or "ashr" or "bitcast"
            or "zext" or "sext" or "trunc" or "fptrunc" or "fpext") return true;
        return Regex.IsMatch(text, @"^call float @dx\.op\.(tertiary\.f32\(i32 46,|unary\.f32\(i32 (6|7|24|25),|binary\.f32\(i32 (35|36|37|38),|dot2\.f32\(i32 54,|dot3\.f32\(i32 55,|dot4\.f32\(i32 56,)");
    }
}
