using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

/// <summary>Rewrites an installed host pixel program so a source material is shaded as the source game shades it.</summary>
internal static class PixelShader
{
    /// <summary>Texture registers by role: the environment cube and each layer's colour and normal.</summary>
    internal sealed record LayerSlots(int Environment, int[] Colour, int[] Normal);

    /// <summary>The host's G-buffer render targets by role, and the weight written beside emitted light.</summary>
    internal sealed record Targets(int Normal, int Colour, int Emission, float EmissionWeight);

    /// <summary>Linear RGB factors: environment gain, upper and lower hemisphere light, and the material's diffuse multiplier.</summary>
    internal sealed record GroundLighting(float[] Environment, float[] Upper, float[] Lower, float[] Diffuse);

    private const string Name = @"%[\w.$-]+";
    private sealed record Instruction(int Line, string Text, string[] Operands);

    /// <summary>
    /// Gives the second layer its own coordinates and frame, blends the layers by vertex alpha, and writes the
    /// source's environment and hemisphere lighting of the blended colour to the emission target.
    /// </summary>
    internal static string ComposeLayeredGround(string module, string vertex, LayerSlots slots, Targets targets, GroundLighting lighting)
    {
        foreach (var factor in new[] { lighting.Environment, lighting.Upper, lighting.Lower, lighting.Diffuse })
            if (factor.Length != 3 || !factor.All(float.IsFinite)) throw new InvalidDataException("Ground lighting factors must be three finite values");
        int[] textures = [slots.Environment, .. slots.Colour, .. slots.Normal];
        if (slots.Colour.Length != 2 || slots.Normal.Length != 2 || textures.Distinct().Count() != 5)
            throw new InvalidDataException("Layered ground needs five distinct texture slots");
        if (module.Contains("%aog_", StringComparison.Ordinal)) throw new InvalidDataException("Pixel program is already composed");

        // Inputs: find where the vertex stage forwards the second coordinates, the vertex colour and both bitangents.
        var produced = new ShaderSignatures(vertex);
        var signatures = new ShaderSignatures(module, "ps");
        var loads = new List<string>();
        var used = new Dictionary<int, int>();
        string Load(string name, int index, int column, string label)
        {
            var member = signatures.Find(false, name, index);
            if (member.ComponentType != 9) throw new InvalidDataException("Unsupported pixel input type: " + name + index);
            used[member.Id] = used.GetValueOrDefault(member.Id) | 1 << column;
            loads.Add($"  %aog_{label} = call float @dx.op.loadInput.f32(i32 4, i32 {member.Id}, i32 0, i8 {column}, i32 undef)");
            return "%aog_" + label;
        }
        string Forwarded(string name, int index, int component, string label)
        {
            var target = VertexShader.Forwarded(vertex, name, index)[component]
                ?? throw new InvalidDataException($"Vertex stage does not forward {name}{index} component {component}");
            var output = produced.Find(true, target.Name, target.Index);
            return Load(target.Name, target.Index, target.Column - produced.Column(output), label);
        }
        string[] second = [Forwarded("TEXCOORD", 1, 0, "u1"), Forwarded("TEXCOORD", 1, 1, "v1")];
        string[] tint = [Forwarded("COLOR", 0, 0, "cr"), Forwarded("COLOR", 0, 1, "cg"), Forwarded("COLOR", 0, 2, "cb")];
        string alpha = Forwarded("COLOR", 0, 3, "alpha");
        var bitangents = new string[2][];
        for (int layer = 0; layer < 2; layer++)
        {
            signatures.Link(produced.Find(true, "TANGENT", 4 + 2 * layer));
            bitangents[layer] = "xyz".Select((axis, i) => Load("TANGENT", 4 + 2 * layer, i, $"b{layer}{axis}")).ToArray();
        }
        foreach (var (id, mask) in used) signatures.Use(false, id, mask);
        int Target(int index) => signatures.Find(true, "SV_Target", index).Id;
        int normalTarget = Target(targets.Normal), colourTarget = Target(targets.Colour), emissionTarget = Target(targets.Emission);
        int normalInput = signatures.Find(false, "NORMAL", 0).Id, tangentInput = signatures.Find(false, "TANGENT", 0).Id;
        int? faceInput = TryFind(signatures, "SV_IsFrontFace");

        var lines = signatures.Lines.ToList();
        int start = lines.FindIndex(l => l == $"define void @{signatures.Entry}() {{");
        int end = lines.FindIndex(start + 1, l => l == "}");
        if (start < 0 || end < 0 || lines.Count(l => l.StartsWith("define ", StringComparison.Ordinal)) != 1)
            throw new InvalidDataException("Expected one pixel entry function");
        // lazy: one forward chain of blocks in a bounded scalar program; loops need a dominance analysis.
        if (end - start > 16384) throw new InvalidDataException("Pixel shader exceeds the bounded scalar adapter");
        var instructions = new Dictionary<string, Instruction>();
        string Code(int line) => lines[line].Split(';')[0].Trim();
        for (int i = start + 1; i < end; i++)
        {
            var match = Regex.Match(Code(i), $"^({Name}) = (.+)$");
            if (match.Success && !instructions.TryAdd(match.Groups[1].Value, new(i, match.Groups[2].Value, Operands(match.Groups[2].Value))))
                throw new InvalidDataException("Duplicate pixel instruction result");
        }
        var closures = new Dictionary<string, HashSet<string>>();
        HashSet<string> Closure(string name)
        {
            if (closures.TryGetValue(name, out var known)) return known;
            var result = new HashSet<string> { name };
            if (instructions.TryGetValue(name, out var instruction))
                foreach (var operand in instruction.Operands) result.UnionWith(Closure(operand));
            return closures[name] = result;
        }

        // Textures: map each register to the handle the program creates for it and to its one sample.
        var handles = new Dictionary<int, string>();
        foreach (int slot in textures)
        {
            var record = lines.Select(l => Regex.Match(l, @"^!\d+ = !\{i32 (\d+), %""class\.Texture2D<vector<float, 4> >""\* undef, !"""", i32 0, i32 (\d+), i32 1, i32 2, i32 0, (?:!\d+|null)\}$"))
                .Where(m => m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) == slot).ToArray();
            if (record.Length != 1) throw new InvalidDataException("Pixel program does not declare one surface texture at texture slot " + slot);
            var created = instructions.Where(p => Regex.IsMatch(p.Value.Text,
                $@"^call %dx\.types\.Handle @dx\.op\.createHandle\(i32 57, i8 0, i32 {record[0].Groups[1].Value}, i32 \d+, i1 false\)$")).ToArray();
            if (created.Length != 1) throw new InvalidDataException("Pixel program does not create one handle for texture slot " + slot);
            handles.Add(slot, created[0].Key);
        }
        var sample = new Regex($@"^call %dx\.types\.ResRet\.f32 @dx\.op\.sample\.f32\(i32 60, %dx\.types\.Handle (?<texture>{Name}), %dx\.types\.Handle (?<sampler>{Name}), float (?<u>{Name}), float (?<v>{Name}), float undef, float undef, i32 0, i32 0, i32 undef, float undef\)$");
        var samples = new Dictionary<int, (string Result, Match Call)>();
        foreach (int slot in textures)
        {
            var uses = instructions.Where(p => p.Value.Operands.Contains(handles[slot])).ToArray();
            if (uses.Length != 1 || !sample.IsMatch(uses[0].Value.Text))
                throw new InvalidDataException("Expected exactly one plain sample of texture slot " + slot);
            samples.Add(slot, (uses[0].Key, sample.Match(uses[0].Value.Text)));
        }
        string sampler = samples[slots.Colour[0]].Call.Groups["sampler"].Value;
        var edits = new Dictionary<int, string>();
        var before = new SortedDictionary<int, List<string>>();
        void Insert(int line, IEnumerable<string> added)
        {
            if (!before.TryGetValue(line, out var list)) before.Add(line, list = []);
            list.AddRange(added);
        }

        // Second-layer coordinates: repeat the host's tiling arithmetic with the second coordinate set as its input.
        bool InputLoad(string name) => instructions.TryGetValue(name, out var i) && i.Text.StartsWith("call float @dx.op.loadInput.f32(", StringComparison.Ordinal);
        string[] Primary(string axis)
        {
            var leaves = Closure(samples[slots.Colour[0]].Call.Groups[axis].Value).Where(InputLoad).ToArray();
            return leaves.Length == 1 ? leaves : throw new InvalidDataException("First-layer texture coordinate does not come from one input");
        }
        string[] primary = [Primary("u")[0], Primary("v")[0]];
        foreach (var (slot, role) in new[] { (slots.Colour[1], "colour"), (slots.Normal[1], "normal") })
        {
            var (sampled, call) = samples[slot];
            var coordinates = new string[2];
            var added = new List<string>();
            for (int axis = 0; axis < 2; axis++)
            {
                string root = call.Groups[axis == 0 ? "u" : "v"].Value;
                var dependent = Closure(root).Where(n => instructions.ContainsKey(n) && Closure(n).Contains(primary[axis])).ToHashSet();
                if (!dependent.Contains(root) || Closure(root).Where(InputLoad).Any(l => l != primary[axis]))
                    throw new InvalidDataException("Second-layer texture coordinate does not follow the first layer's input");
                var names = dependent.OrderBy(n => instructions[n].Line).Select((n, i) => (n, fresh: $"%aog_{role}{"uv"[axis]}{i}")).ToDictionary(p => p.n, p => p.fresh);
                names[primary[axis]] = second[axis];
                foreach (string name in dependent.OrderBy(n => instructions[n].Line))
                {
                    if (name == primary[axis]) continue;
                    string text = instructions[name].Text;
                    if (!Regex.IsMatch(text, @"^f(add|sub|mul|div)(?: fast)? float ")) throw new InvalidDataException("Unsupported texture coordinate arithmetic");
                    added.Add($"  {names[name]} = {Regex.Replace(text, Name, m => names.GetValueOrDefault(m.Value, m.Value))}");
                }
                coordinates[axis] = names[root];
            }
            Insert(instructions[sampled].Line, added);
            edits[instructions[sampled].Line] = $"  {sampled} = {call.Value.Replace($"float {call.Groups["u"].Value}, float {call.Groups["v"].Value}", $"float {coordinates[0]}, float {coordinates[1]}")}";
        }

        // Layer blend: the host blends both colours by one weight; the source blends by vertex alpha.
        bool From(string name, int slot) => Closure(name).Contains(samples[slot].Result);
        var blends = new List<(string Weight, string Sum, int Channel)>();
        foreach (var (name, instruction) in instructions)
        {
            var product = Regex.Match(instruction.Text, $"^fmul(?: fast)? float ({Name}), ({Name})$");
            if (!product.Success) continue;
            foreach (var (weight, difference) in new[] { (product.Groups[1].Value, product.Groups[2].Value), (product.Groups[2].Value, product.Groups[1].Value) })
            {
                if (!instructions.TryGetValue(difference, out var subtract)) continue;
                var parts = Regex.Match(subtract.Text, $"^fsub(?: fast)? float ({Name}), ({Name})$");
                if (!parts.Success || From(weight, slots.Colour[0]) || From(weight, slots.Colour[1])) continue;
                string upper = parts.Groups[1].Value, lower = parts.Groups[2].Value;
                if (!From(upper, slots.Colour[1]) || From(upper, slots.Colour[0]) || !From(lower, slots.Colour[0]) || From(lower, slots.Colour[1])) continue;
                var sums = instructions.Where(p => Regex.IsMatch(p.Value.Text, "^fadd(?: fast)? float ") && p.Value.Operands.Length == 2
                    && p.Value.Operands.Contains(name) && p.Value.Operands.Contains(lower)).ToArray();
                var channels = Closure(lower).Where(n => instructions.TryGetValue(n, out var i)
                    && Regex.IsMatch(i.Text, $@"^extractvalue %dx\.types\.ResRet\.f32 {Regex.Escape(samples[slots.Colour[0]].Result)}, \d$")).ToArray();
                if (sums.Length != 1 || channels.Length != 1) throw new InvalidDataException("Ambiguous layer colour blend");
                blends.Add((weight, sums[0].Key, instructions[channels[0]].Text[^1] - '0'));
            }
        }
        if (blends.Count != 3 || blends.Select(b => b.Weight).Distinct().Count() != 1
            || !blends.Select(b => b.Channel).Order().SequenceEqual([0, 1, 2]) || !instructions.ContainsKey(blends[0].Weight))
            throw new InvalidDataException("Expected one three-channel layer colour blend");
        string weightName = blends[0].Weight;
        edits[instructions[weightName].Line] = $"  {weightName} = fadd fast float {alpha}, 0.000000e+00";
        var blended = blends.OrderBy(b => b.Channel).Select(b => b.Sum).ToArray();

        // World normal: the three values the host normalises and encodes into its normal target.
        var stored = new Regex($@"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 (?<id>\d+), i32 0, i8 (?<column>[0-3]), float (?<value>\S+)\)$");
        var normalStores = Enumerable.Range(start + 1, end - start - 1).Select(i => stored.Match(Code(i)))
            .Where(m => m.Success && int.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture) == normalTarget
                && m.Groups["column"].Value != "3" && instructions.ContainsKey(m.Groups["value"].Value)).ToArray();
        if (normalStores.Length != 3) throw new InvalidDataException("Expected one computed store for each normal target axis");
        var lengths = instructions.Where(p =>
        {
            var reciprocal = Regex.Match(p.Value.Text, $@"^call float @dx\.op\.unary\.f32\(i32 25, float ({Name})\)$");
            if (!reciprocal.Success || !instructions.TryGetValue(reciprocal.Groups[1].Value, out var dot)) return false;
            var self = Regex.Match(dot.Text, $@"^call float @dx\.op\.dot3\.f32\(i32 55, float ({Name}), float ({Name}), float ({Name}), float \1, float \2, float \3\)$");
            return self.Success && normalStores.All(s => Closure(s.Groups["value"].Value).Contains(p.Key));
        }).OrderByDescending(p => p.Value.Line).ToArray();
        if (lengths.Length == 0) throw new InvalidDataException("Normal target is not written from a normalised vector");
        string reciprocalLength = lengths[0].Key;
        var axes = instructions[instructions[reciprocalLength].Operands[0]].Operands.Take(3).ToArray();
        if (axes.Distinct().Count() != 3 || axes.Any(a => !instructions.ContainsKey(a))) throw new InvalidDataException("Unsupported host normal vector");
        bool Sampled(string name) => textures.Any(slot => From(name, slot));
        HashSet<int> Inputs(string name) => Closure(name).Where(n => instructions.TryGetValue(n, out var i) && i.Text.Contains("@dx.op.loadInput.", StringComparison.Ordinal))
            .Select(n => int.Parse(Regex.Match(instructions[n].Text, @"\(i32 4, i32 (\d+),").Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet();
        var surfaceNormal = new string[3]; var surfaceTangent = new string[3];
        for (int axis = 0; axis < 3; axis++)
        {
            var normals = new HashSet<string>(); var tangents = new HashSet<string>();
            void Visit(string name)
            {
                if (!instructions.TryGetValue(name, out var instruction)) return;
                if (!Sampled(name))
                {
                    var inputs = Inputs(name);
                    if (faceInput is { } face) inputs.Remove(face);
                    if (inputs.SetEquals([normalInput])) normals.Add(name);
                    else if (inputs.SetEquals([tangentInput])) tangents.Add(name);
                    return;
                }
                if (!Regex.IsMatch(instruction.Text, @"^(fmul|fadd|fsub)(?: fast)? float |^call float @dx\.op\.tertiary\.f32\(i32 46,")) return;
                foreach (var operand in instruction.Operands) Visit(operand);
            }
            Visit(axes[axis]);
            if (normals.Count != 1 || tangents.Count != 1) throw new InvalidDataException("Host normal vector lacks one surface normal and tangent per axis");
            surfaceNormal[axis] = normals.Single(); surfaceTangent[axis] = tangents.Single();
        }
        int normalLine = axes.Min(a => instructions[a].Line);
        var body = new List<string>();
        string Emit(string label, string expression) { body.Add($"  %aog_{label} = {expression}"); return "%aog_" + label; }
        string Dot3(string label, string[] v) => Emit(label, $"call float @dx.op.dot3.f32(i32 55, float {v[0]}, float {v[1]}, float {v[2]}, float {v[0]}, float {v[1]}, float {v[2]})");
        string Unary(string label, int code, string value) => Emit(label, $"call float @dx.op.unary.f32(i32 {code}, float {value})");
        string Mad(string label, string a, string b, string c) => Emit(label, $"call float @dx.op.tertiary.f32(i32 46, float {a}, float {b}, float {c})");
        string[] Normalize(string label, string[] v)
        {
            string inverse = Unary(label + "inv", 25, Dot3(label + "len", v));
            return "xyz".Select((axis, i) => Emit($"{label}{axis}", $"fmul fast float {v[i]}, {inverse}")).ToArray();
        }
        var layers = new string[2][];
        for (int layer = 0; layer < 2; layer++)
        {
            string packed = samples[slots.Normal[layer]].Result;
            var bitangent = Normalize($"bn{layer}", bitangents[layer]);
            var xy = "xy".Select((axis, i) => Emit($"n{layer}{axis}", "fadd fast float " + Emit($"n{layer}{axis}t", "fmul fast float "
                + Emit($"n{layer}{axis}s", $"extractvalue %dx.types.ResRet.f32 {packed}, {i}") + ", 2.000000e+00") + ", -1.000000e+00")).ToArray();
            string z = Unary($"n{layer}z", 24, Emit($"n{layer}remain", "fsub fast float 1.000000e+00, " + Unary($"n{layer}clipped", 7,
                Emit($"n{layer}length", $"call float @dx.op.dot2.f32(i32 54, float {xy[0]}, float {xy[1]}, float {xy[0]}, float {xy[1]})"))));
            // The source builds each layer's normal from its own bitangent with the shared tangent and surface normal.
            layers[layer] = Normalize($"normalized{layer}", "xyz".Select((axis, i) => Mad($"w{layer}{axis}", surfaceNormal[i], z,
                Mad($"w{layer}{axis}b", surfaceTangent[i], xy[1], Emit($"w{layer}{axis}a", $"fmul fast float {bitangent[i]}, {xy[0]}")))).ToArray());
        }
        var deltas = "xyz".Select((axis, i) => Emit("delta" + axis, $"fsub fast float {layers[1][i]}, {layers[0][i]}")).ToArray();
        if (new[] { samples[slots.Normal[0]].Result, samples[slots.Normal[1]].Result }.Concat(surfaceNormal).Concat(surfaceTangent).Any(n => instructions[n].Line > normalLine))
            throw new InvalidDataException("Host normal vector precedes its inputs");
        Insert(normalLine, body);
        for (int axis = 0; axis < 3; axis++)
            edits[instructions[axes[axis]].Line] = $"  {axes[axis]} = call float @dx.op.tertiary.f32(i32 46, float {deltas[axis]}, float {alpha}, float {layers[0][axis]})";

        // Source lighting of the blended colour, sampled along the final unit normal.
        body = [];
        int litLine = Math.Max(instructions[reciprocalLength].Line, blended.Max(b => instructions[b].Line)) + 1;
        var unit = "xyz".Select((axis, i) => Emit("envn" + axis, $"fmul fast float {axes[i]}, {reciprocalLength}")).ToArray();
        string environment = Emit("environment", $"call %dx.types.ResRet.f32 @dx.op.sampleLevel.f32(i32 62, %dx.types.Handle {handles[slots.Environment]}, %dx.types.Handle {sampler}, float {unit[0]}, float {unit[1]}, float {unit[2]}, float undef, i32 undef, i32 undef, i32 undef, float 0.000000e+00)");
        string hemisphere = Mad("hemiweight", unit[1], "5.000000e-01", "5.000000e-01");
        var lit = new string[3];
        for (int i = 0; i < 3; i++)
        {
            char c = "rgb"[i];
            // The source multiplies in its gamma space, then converts to linear light.
            string colour = Unary("color" + c, 21, Emit("power" + c, "fmul fast float " + Unary("log" + c, 23,
                Emit("encoded" + c, $"fmul fast float {blended[i]}, {tint[i]}")) + ", " + Float(2.2f)));
            string light = Mad("light" + c, Emit("env" + c, $"extractvalue %dx.types.ResRet.f32 {environment}, {i}"), Float(lighting.Environment[i]),
                Mad("hemi" + c, hemisphere, Float(lighting.Upper[i] - lighting.Lower[i]), Float(lighting.Lower[i])));
            lit[i] = Emit("sourcelit" + c, $"fmul fast float {light}, " + Emit("sourcealbedo" + c, $"fmul fast float {colour}, {Float(lighting.Diffuse[i])}"));
        }
        Insert(litLine, body);
        var replaced = new HashSet<(int, int)>();
        for (int i = litLine; i < end; i++)
        {
            var store = stored.Match(Code(i));
            if (!store.Success) continue;
            int id = int.Parse(store.Groups["id"].Value, CultureInfo.InvariantCulture), column = store.Groups["column"].Value[0] - '0';
            // With no surface colour left, the host's own lighting adds nothing to the light emitted here.
            string? value = id == colourTarget && column < 3 ? "0.000000e+00" : id == emissionTarget ? column < 3 ? lit[column] : Float(targets.EmissionWeight) : null;
            if (value is null) continue;
            if (!replaced.Add((id, column))) throw new InvalidDataException("Ambiguous colour or emission target store");
            edits[i] = $"  call void @dx.op.storeOutput.f32(i32 5, i32 {id}, i32 0, i8 {column}, float {value})";
        }
        if (replaced.Count != 7) throw new InvalidDataException("Expected final colour and emission target stores after the lighting");
        for (int i = litLine; i < end; i++)
            if (Regex.IsMatch(Code(i), @"^(br|switch) ")) throw new InvalidDataException("Unsupported control flow after the host normal");

        // The environment slot becomes a cube; the host's own surface sample of it no longer applies.
        var (edge, edgeCall) = samples[slots.Environment];
        edits[instructions[edge].Line] = $"  {edge} = call %dx.types.ResRet.f32 @dx.op.sampleLevel.f32(i32 62, %dx.types.Handle {handles[slots.Environment]}, %dx.types.Handle {edgeCall.Groups["sampler"].Value}, float 1.000000e+00, float 0.000000e+00, float 0.000000e+00, float undef, i32 undef, i32 undef, i32 undef, float 0.000000e+00)";
        int resource = lines.FindIndex(l => Regex.IsMatch(l, $@"^!\d+ = !\{{i32 \d+, %""class\.Texture2D<vector<float, 4> >""\* undef, !"""", i32 0, i32 {slots.Environment}, i32 1, i32 2, i32 0, "));
        lines[resource] = lines[resource].Replace("%\"class.Texture2D<vector<float, 4> >\"* undef", "%\"class.TextureCube<vector<float, 4> >\"* undef")
            .Replace($"i32 0, i32 {slots.Environment}, i32 1, i32 2, i32 0, ", $"i32 0, i32 {slots.Environment}, i32 1, i32 5, i32 0, ");

        var result = new List<string>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i == start && !lines.Any(l => l.StartsWith("%\"class.TextureCube<vector<float, 4> >\" = type", StringComparison.Ordinal)))
                result.AddRange(["%\"class.TextureCube<vector<float, 4> >\" = type { <4 x float>, %\"class.TextureCube<vector<float, 4> >::mips_type\" }",
                    "%\"class.TextureCube<vector<float, 4> >::mips_type\" = type { i32 }", ""]);
            if (before.TryGetValue(i, out var added)) result.AddRange(added);
            result.Add(edits.GetValueOrDefault(i, lines[i]));
            if (i == start) result.AddRange(loads);
        }
        foreach (var (function, declaration) in new[]
        {
            ("loadInput.f32", "declare float @dx.op.loadInput.f32(i32, i32, i32, i8, i32)"),
            ("unary.f32", "declare float @dx.op.unary.f32(i32, float)"),
            ("tertiary.f32", "declare float @dx.op.tertiary.f32(i32, float, float, float)"),
            ("dot2.f32", "declare float @dx.op.dot2.f32(i32, float, float, float, float)"),
            ("dot3.f32", "declare float @dx.op.dot3.f32(i32, float, float, float, float, float, float)"),
            ("sampleLevel.f32", "declare %dx.types.ResRet.f32 @dx.op.sampleLevel.f32(i32, %dx.types.Handle, %dx.types.Handle, float, float, float, float, i32, i32, i32, float)"),
        })
            if (!lines.Any(l => l.StartsWith("declare ", StringComparison.Ordinal) && l.Contains($"@dx.op.{function}(", StringComparison.Ordinal)))
                result.Add(declaration);
        return string.Join('\n', result) + "\n";
    }

    private static int? TryFind(ShaderSignatures signatures, string name)
    {
        try { return signatures.Find(false, name, 0).Id; }
        catch (InvalidDataException) { return null; }
    }

    // A float constant in the assembler's exact form: the bits of the equal double.
    private static string Float(float value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
    private static string[] Operands(string text) => Regex.Matches(text, Name).Select(m => m.Value).ToArray();
}
