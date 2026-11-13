using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

/// <summary>Rewrites an installed host particle pixel program so marked particles are shaded as the source game shades its own.</summary>
internal static class EffectShader
{
    /// <summary>The source's fog over view depth: from <c>Begin</c> at rate <c>Scale</c> up to <c>Limit</c>.</summary>
    internal sealed record Fog(float Begin, float Scale, float Limit);

    private const string Name = @"%[\w.$-]+";

    /// <summary>
    /// The host splits a particle's colour into direction and strength and squares the image; the source raises the image
    /// times the whole colour to its display power and fades opacity with its fog. A particle whose alpha bias carries
    /// <paramref name="marker"/> takes the source's response; every other particle keeps the host's.
    /// </summary>
    // lazy: the source's light scattering and its sharper image filtering are not applied to particles.
    internal static string RespondLikeSource(string module, float marker, Fog fog)
    {
        // A whole number well outside any real alpha bias, so no installed effect carries it by accident.
        if (!(marker >= 256) || marker != MathF.Floor(marker)) throw new InvalidDataException("Particle marker must be a whole number above any alpha bias");
        if (!float.IsFinite(fog.Begin) || !(fog.Scale > 0) || fog.Limit is < 0 or > 1) throw new InvalidDataException("Invalid source fog for particles");
        if (module.Contains("%aog_", StringComparison.Ordinal)) throw new InvalidDataException("Particle stage is already rewritten");
        var signatures = new ShaderSignatures(module, "ps");
        var colourInput = signatures.Find(false, "COLOR", 0);
        // The source's response reads every channel of the particle colour.
        signatures.Use(false, colourInput.Id, 0b1111);
        var lines = signatures.Lines.ToList();
        int start = lines.FindIndex(l => l == $"define void @{signatures.Entry}() {{");
        int end = lines.FindIndex(start + 1, l => l == "}");
        if (start < 0 || end < 0) throw new InvalidDataException("Expected one particle entry function");
        string Code(int line) => lines[line].Split(';')[0].Trim();
        var instructions = new Dictionary<string, (int Line, string Text)>();
        for (int i = start + 1; i < end; i++)
        {
            var match = Regex.Match(Code(i), $"^({Name}) = (.+)$");
            if (match.Success) instructions[match.Groups[1].Value] = (i, match.Groups[2].Value);
        }
        string[] Operands(string name) => instructions.TryGetValue(name, out var i) ? Regex.Matches(i.Text, Name).Select(m => m.Value).ToArray() : [];
        var closures = new Dictionary<string, HashSet<string>>();
        HashSet<string> Closure(string name)
        {
            if (closures.TryGetValue(name, out var known)) return known;
            var result = new HashSet<string> { name };
            foreach (var operand in Operands(name)) result.UnionWith(Closure(operand));
            return closures[name] = result;
        }
        int firstLabel = Enumerable.Range(start + 1, end - start - 1).FirstOrDefault(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:"), end);

        // The particle's image is the texture the host's own reflection names; its two samples are the two frames being blended.
        var bound = lines.Select(l => Regex.Match(l, @"^; g_diffuseTexture\s+texture\s+\S+\s+2d\s+T(\d+)\s+t\d+\s+1$")).Where(m => m.Success).ToArray();
        if (bound.Length != 1) throw new InvalidDataException("Particle stage does not declare one particle image");
        var handles = instructions.Where(p => Regex.IsMatch(p.Value.Text, $@"^call %dx\.types\.Handle @dx\.op\.createHandle\(i32 57, i8 0, i32 {bound[0].Groups[1].Value}, i32 \d+, i1 false\)$")).ToArray();
        if (handles.Length != 1) throw new InvalidDataException("Particle stage does not open its particle image once");
        var frames = instructions.Where(p => Regex.IsMatch(p.Value.Text, $@"^call %dx\.types\.ResRet\.f32 @dx\.op\.sample\.f32\(i32 60, %dx\.types\.Handle {Regex.Escape(handles[0].Key)}, "))
            .OrderBy(p => p.Value.Line).Select(p => p.Key).ToArray();
        if (frames.Length != 2 || frames.Any(f => instructions[f].Line > firstLabel)) throw new InvalidDataException("Particle stage does not blend two frames of its image");
        string Channel(string frame, int channel) => instructions.SingleOrDefault(p => p.Value.Text == $"extractvalue %dx.types.ResRet.f32 {frame}, {channel}").Key
            ?? throw new InvalidDataException("Particle stage does not read every channel of both frames");
        bool Input(string name) => instructions.TryGetValue(name, out var i) && i.Text.StartsWith("call float @dx.op.loadInput.f32(", StringComparison.Ordinal);
        // The blend fraction scales the difference between the frames in every channel.
        var fractions = new HashSet<string>();
        for (int channel = 0; channel < 4; channel++)
        {
            string difference = instructions.SingleOrDefault(p => Regex.IsMatch(p.Value.Text, $@"^fsub(?: fast)? float {Regex.Escape(Channel(frames[1], channel))}, {Regex.Escape(Channel(frames[0], channel))}$")).Key
                ?? throw new InvalidDataException("Particle stage does not blend two frames of its image");
            foreach (var product in instructions.Values.Select(i => Regex.Match(i.Text, $"^fmul(?: fast)? float ({Name}), ({Name})$")).Where(m => m.Success))
                if (product.Groups[1].Value == difference) fractions.Add(product.Groups[2].Value);
                else if (product.Groups[2].Value == difference) fractions.Add(product.Groups[1].Value);
        }
        if (fractions.Count != 1 || !Input(fractions.Single())) throw new InvalidDataException("Particle stage does not blend its frames by one input fraction");
        string fraction = fractions.Single();
        // The alpha bias is the one other input reaching the first clamp of the blended alpha.
        var coordinates = frames.SelectMany(f => Closure(f).Where(Input)).ToHashSet();
        var clamp = instructions.Where(p => Regex.IsMatch(p.Value.Text, @"^call float @dx\.op\.unary\.f32\(i32 7, ") && Closure(p.Key).Contains(Channel(frames[0], 3)) && Closure(p.Key).Contains(Channel(frames[1], 3)))
            .OrderBy(p => p.Value.Line).Select(p => p.Key).FirstOrDefault() ?? throw new InvalidDataException("Particle stage does not clamp its blended alpha");
        var biases = Closure(clamp).Where(Input).Where(n => n != fraction && !coordinates.Contains(n)).ToArray();
        if (biases.Length != 1) throw new InvalidDataException("Particle stage does not add one alpha bias");
        // View depth divides the screen position's other components.
        var depths = instructions.Values.Select(i => Regex.Match(i.Text, $"^fdiv(?: fast)? float ({Name}), ({Name})$")).Where(m => m.Success && Input(m.Groups[1].Value) && Input(m.Groups[2].Value))
            .GroupBy(m => m.Groups[2].Value).Where(g => g.Select(m => m.Groups[1].Value).Distinct().Count() >= 2).Select(g => g.Key).ToArray();
        if (depths.Length != 1) throw new InvalidDataException("Particle stage does not expose one view depth");
        foreach (string needed in new[] { fraction, biases[0], depths[0] })
            if (instructions[needed].Line > firstLabel) throw new InvalidDataException("Particle stage reads a needed input on a conditional path");

        // The final writes: colour and opacity to the first target, opacity to the second.
        var stored = new Regex(@"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 (?<id>\d+), i32 0, i8 (?<column>[0-3]), float (?<value>\S+)\)$");
        int lastLabel = Enumerable.Range(start + 1, end - start - 1).LastOrDefault(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:"), start);
        var stores = Enumerable.Range(lastLabel + 1, end - lastLabel - 1).Select(i => (Line: i, Match: stored.Match(Code(i)))).Where(s => s.Match.Success).ToArray();
        int scene = signatures.Find(true, "SV_Target", 0).Id, mask = signatures.Find(true, "SV_Target", 1).Id;
        if (stores.Length != 8 || Enumerable.Range(start + 1, Math.Max(0, lastLabel - start - 1)).Any(i => stored.IsMatch(Code(i))))
            throw new InvalidDataException("Particle stage does not end with one write of each target channel");

        var body = new List<string>();
        string Emit(string label, string expression) { body.Add($"  %aog_{label} = {expression}"); return "%aog_" + label; }
        string Unary(string label, int code, string value) => Emit(label, $"call float @dx.op.unary.f32(i32 {code}, float {value})");
        var loads = new List<string>();
        string Colour(int column)
        {
            loads.Add($"  %aog_colour{column} = call float @dx.op.loadInput.f32(i32 4, i32 {colourInput.Id}, i32 0, i8 {column}, i32 undef)");
            return $"%aog_colour{column}";
        }
        string marked = Emit("marked", $"fcmp oeq float {Unary("bias", 6, biases[0])}, {Float(marker)}");
        string Blend(int channel, string label) => Emit(label, $"fadd float {Channel(frames[0], channel)}, " + Emit(label + "step", "fmul float "
            + Emit(label + "delta", $"fsub float {Channel(frames[1], channel)}, {Channel(frames[0], channel)}") + $", {fraction}"));
        var response = new string[4];
        for (int c = 0; c < 3; c++)
            response[c] = Unary("lit" + c, 21, Emit("power" + c, "fmul float " + Unary("log" + c, 23, Unary("abs" + c, 6,
                Emit("tinted" + c, $"fmul float {Blend(c, "texel" + c)}, {Colour(c)}"))) + ", " + Float(2.2f)));
        string fogged = Emit("clear", "fsub float 1.000000e+00, " + Emit("fogweight", "fmul float " + Unary("fog", 7, Emit("fogramp", "fmul float "
            + Emit("fogdepth", $"fsub float {depths[0]}, {Float(fog.Begin)}") + ", " + Float(fog.Scale))) + ", " + Float(fog.Limit)));
        response[3] = Unary("opacity", 7, Emit("faded", "fmul float " + Emit("covered", $"fmul float {Unary("coverage", 7, Blend(3, "texel3"))}, {Colour(3)}") + $", {fogged}"));
        var edits = new Dictionary<int, string>();
        var seen = new HashSet<(int, int)>();
        foreach (var (line, match) in stores)
        {
            int id = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture), column = match.Groups["column"].Value[0] - '0';
            if (!seen.Add((id, column)) || id != scene && id != mask) throw new InvalidDataException("Particle stage does not end with one write of each target channel");
            if (id == mask && column < 3) continue;
            string chosen = Emit($"out{id}{column}", $"select i1 {marked}, float {response[column]}, float {match.Groups["value"].Value}");
            edits[line] = $"  call void @dx.op.storeOutput.f32(i32 5, i32 {id}, i32 0, i8 {column}, float {chosen})";
        }
        var result = new List<string>();
        var current = lines;
        for (int i = 0; i < current.Count; i++)
        {
            if (i == stores[0].Line) result.AddRange(body);
            result.Add(edits.GetValueOrDefault(i, current[i]));
            if (i == start) result.AddRange(loads);
        }
        foreach (var (function, declaration) in new[] { ("unary.f32", "declare float @dx.op.unary.f32(i32, float)") })
            if (!current.Any(l => l.StartsWith("declare ", StringComparison.Ordinal) && l.Contains($"@dx.op.{function}(", StringComparison.Ordinal))) result.Add(declaration);
        return string.Join('\n', result) + "\n";
    }

    private static string Float(float value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
}
