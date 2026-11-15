using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

internal static class VertexShader
{
    /// <summary>The first output register this vertex program leaves free.</summary>
    internal static int FreeOutputRow(string module) => new ShaderSignatures(module).FreeRow(true);

    /// <summary>
    /// Forwards a second tangent and both bitangents. Variants of one family share a pixel stage, so the caller can
    /// name the first output register for all of them.
    /// </summary>
    internal static string AddLayerFrames(string module, int? outputRow = null) =>
        AddDirection(AddDirection(AddDirection(module, 2, true, outputRow), 4, false, outputRow + 1), 6, false, outputRow + 2);

    internal static string AddSecondaryTangent(string module) => AddDirection(module, 2, true, null);

    /// <summary>For each component of a vertex input, the output semantic and column that receives it unchanged, if any.</summary>
    internal static (string Name, int Index, int Column)?[] Forwarded(string module, string name, int index)
    {
        var signatures = new ShaderSignatures(module);
        var input = signatures.Find(false, name, index);
        var lines = signatures.Lines;
        var result = new (string, int, int)?[input.Columns];
        var loads = new Dictionary<string, int>();
        foreach (var line in lines)
        {
            var load = Regex.Match(line.Split(';')[0].Trim(), @"^(%[\w.$-]+) = call float @dx\.op\.loadInput\.f32\(i32 4, i32 " + input.Id + @", i32 0, i8 ([0-3]), i32 undef\)$");
            if (load.Success) loads.Add(load.Groups[1].Value, load.Groups[2].Value[0] - '0');
        }
        var outputs = Enumerable.Range(0, 64).Select(id => { try { return signatures.Output(id); } catch (InvalidDataException) { return null; } })
            .TakeWhile(m => m is not null).ToArray();
        foreach (var line in lines)
        {
            var store = Regex.Match(line.Split(';')[0].Trim(), @"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 (\d+), i32 0, i8 ([0-3]), float (%[\w.$-]+)\)$");
            if (!store.Success || !loads.TryGetValue(store.Groups[3].Value, out int component)) continue;
            var output = outputs[int.Parse(store.Groups[1].Value)]!;
            var target = (output.Name, output.Index, signatures.Column(output) + (store.Groups[2].Value[0] - '0'));
            if (result[component] is { } previous && previous != target) throw new InvalidDataException("Vertex input is forwarded to several outputs");
            result[component] = target;
        }
        return result;
    }

    /// <summary>
    /// Forwards the world-space vector from the vertex to the camera, with the view depth as its fourth component (TANGENT8).
    /// The installed scenery programs compute that vector themselves to output its length; the installed character programs
    /// take the position relative to the camera before projecting it. This finds the vector in either place.
    /// </summary>
    internal static string AddView(string module, int outputRow)
    {
        if (module.Contains("%aog_view", StringComparison.Ordinal)) throw new InvalidDataException("View vector is already forwarded");
        var signatures = new ShaderSignatures(module);
        int position = signatures.Find(true, "SV_Position", 0).Id;
        int view = signatures.Append(true, signatures.Require(true, "TANGENT", 0, 9), 8, null, outputRow);
        var lines = signatures.Lines;
        int start = Array.FindIndex(lines, l => l == $"define void @{signatures.Entry}() {{");
        int end = Array.FindIndex(lines, start + 1, l => l == "}");
        if (start < 0 || end < 0) throw new InvalidDataException("Expected one vertex entry function");
        var instructions = new Dictionary<string, Instruction>();
        for (int i = start + 1; i < end; i++)
        {
            var match = Regex.Match(lines[i].Split(';')[0].Trim(), @"^(%[\w.$-]+) = (.+)$");
            if (match.Success) instructions[match.Groups[1].Value] = new(i, match.Groups[2].Value, Operands(match.Groups[2].Value));
        }
        HashSet<string> Closure(string name, HashSet<string>? seen = null)
        {
            seen ??= [];
            if (seen.Add(name) && instructions.TryGetValue(name, out var instruction)) foreach (var operand in instruction.Operands) Closure(operand, seen);
            return seen;
        }
        // A length written to an output: the root of three squares, each of a constant-buffer value minus a computed position.
        var vectors = new List<string[]>(); (int Line, string Value)? depth = null; int last = -1;
        for (int i = start + 1; i < end; i++)
        {
            var store = Regex.Match(lines[i].Split(';')[0].Trim(), @"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 (\d+), i32 0, i8 ([0-3]), float (%[\w.$-]+)\)$");
            if (!store.Success || !instructions.TryGetValue(store.Groups[3].Value, out var stored)) continue;
            last = i;
            if (int.Parse(store.Groups[1].Value) == position && store.Groups[2].Value == "3") depth = (i, store.Groups[3].Value);
            var root = Regex.Match(stored.Text, @"^call float @dx\.op\.unary\.f32\(i32 24, float (%[\w.$-]+)\)$");
            if (!root.Success) continue;
            var squares = Closure(root.Groups[1].Value).Select(n => instructions.TryGetValue(n, out var square)
                ? Regex.Match(square.Text, @"^fmul(?: fast)? float (%[\w.$-]+), \1$") : Match.Empty).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToArray();
            var axes = new string?[3];
            foreach (string component in squares)
            {
                if (!instructions.TryGetValue(component, out var difference)) continue;
                var parts = Regex.Match(difference.Text, @"^fsub(?: fast)? float (%[\w.$-]+), (%[\w.$-]+)$");
                if (!parts.Success || !instructions.TryGetValue(parts.Groups[1].Value, out var constant)) continue;
                var axis = Regex.Match(constant.Text, @"^extractvalue %dx\.types\.CBufRet\.f32 %[\w.$-]+, ([0-2])$");
                if (axis.Success) axes[axis.Groups[1].Value[0] - '0'] = component;
            }
            if (squares.Length == 3 && axes.All(a => a is not null)) vectors.Add(axes!);
        }
        var distinct = vectors.Select(v => string.Join(",", v)).Distinct().ToArray();
        var reversed = new List<string>();
        if (distinct.Length == 0 && depth is not null)
        {
            // No length is written: the view depth is projected from a position less three values of one constant row.
            var projected = Closure(depth.Value.Value);
            var relative = new Dictionary<string, string?[]>(StringComparer.Ordinal);
            foreach (string value in projected)
            {
                if (!instructions.TryGetValue(value, out var difference)) continue;
                var parts = Regex.Match(difference.Text, @"^fsub(?: fast)? float (%[\w.$-]+), (%[\w.$-]+)$");
                if (!parts.Success || !instructions.TryGetValue(parts.Groups[2].Value, out var constant)) continue;
                var axis = Regex.Match(constant.Text, @"^extractvalue %dx\.types\.CBufRet\.f32 (%[\w.$-]+), ([0-2])$");
                if (!axis.Success) continue;
                if (!relative.TryGetValue(axis.Groups[1].Value, out var axes)) relative.Add(axis.Groups[1].Value, axes = new string?[3]);
                if (axes[axis.Groups[2].Value[0] - '0'] is not null) throw new InvalidDataException("Vertex program takes a position from the camera twice");
                axes[axis.Groups[2].Value[0] - '0'] = value;
            }
            var whole = relative.Values.Where(axes => axes.All(a => a is not null)).ToArray();
            if (whole.Length == 1)
                for (int axis = 0; axis < 3; axis++)
                {
                    // The program's own vector runs from the camera to the vertex; the forwarded one runs back.
                    var parts = Regex.Match(instructions[whole[0][axis]!].Text, @"^fsub(?: fast)? float (%[\w.$-]+), (%[\w.$-]+)$");
                    reversed.Add($"  %aog_view{axis} = fsub fast float {parts.Groups[2].Value}, {parts.Groups[1].Value}");
                    distinct = [$"%aog_view0,%aog_view1,%aog_view2"];
                }
        }
        if (distinct.Length != 1 || depth is null) throw new InvalidDataException("Vertex program does not compute one camera vector and view depth");
        var vector = distinct[0].Split(',');
        if (vector.Append(depth.Value.Value).Any(v => instructions.TryGetValue(v, out var made) && made.Line > last)) throw new InvalidDataException("Camera vector follows the program's outputs");
        var body = reversed.Concat(vector.Append(depth.Value.Value).Select((value, column) => $"  call void @dx.op.storeOutput.f32(i32 5, i32 {view}, i32 0, i8 {column}, float {value})"));
        // Every path writes the new output, as the installed programs do for their own.
        var zeros = Enumerable.Range(0, 4).Select(column => $"  call void @dx.op.storeOutput.f32(i32 5, i32 {view}, i32 0, i8 {column}, float 0.000000e+00)");
        return string.Join('\n', lines.SelectMany((line, i) => i == start ? [line, .. zeros] : i == last ? [line, .. body] : new[] { line })) + "\n";
    }

    private static string AddDirection(string module, int index, bool packed, int? outputRow)
    {
        var signatures = new ShaderSignatures(module);
        var input = signatures.Require(false, "TANGENT", 0, 5);
        var output = signatures.Require(true, "TANGENT", 0, 9);
        int newInput = signatures.Append(false, input, index, packed ? 5 : 9);
        int newOutput = signatures.Append(true, output, index, null, outputRow);
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
