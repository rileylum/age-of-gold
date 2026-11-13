using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

/// <summary>Rewrites the installed host tone-map stage to show the scene as the source game displays it.</summary>
internal static class DisplayShader
{
    private const string Name = @"%[\w.$-]+";

    /// <summary>
    /// The source multiplies its linear scene by an exposure, clamps it and shows it through the display's gamma.
    /// The host's tone curve, bloom tint and colour grade no longer reach the output.
    /// </summary>
    // lazy: one fixed exposure and standard-range output; the source's adaptation, bloom and tone correction are separate translations.
    internal static string DisplayLikeSource(string module, float exposure)
    {
        if (!(exposure > 0) || !float.IsFinite(exposure)) throw new InvalidDataException("Display exposure must be positive");
        if (module.Contains("%aog_", StringComparison.Ordinal)) throw new InvalidDataException("Display stage is already rewritten");
        var signatures = new ShaderSignatures(module, "ps");
        int target = signatures.Find(true, "SV_Target", 0).Id;
        var lines = signatures.Lines.ToList();
        int start = lines.FindIndex(l => l == $"define void @{signatures.Entry}() {{");
        int end = lines.FindIndex(start + 1, l => l == "}");
        if (start < 0 || end < 0) throw new InvalidDataException("Expected one display entry function");
        string Code(int line) => lines[line].Split(';')[0].Trim();
        var instructions = new Dictionary<string, (int Line, string Text)>();
        for (int i = start + 1; i < end; i++)
        {
            var match = Regex.Match(Code(i), $"^({Name}) = (.+)$");
            if (match.Success) instructions[match.Groups[1].Value] = (i, match.Groups[2].Value);
        }
        int firstLabel = Enumerable.Range(start + 1, end - start - 1).FirstOrDefault(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:"), end);
        // The scene is the one texture whose three channels are scaled by the same constant-buffer scalar: the host's exposure.
        var scenes = new List<string>();
        foreach (var (name, sample) in instructions.Where(p => Regex.IsMatch(p.Value.Text, @"^call %dx\.types\.ResRet\.f32 @dx\.op\.sample(Level)?\.f32\(")))
        {
            var channels = instructions.Where(p => Regex.IsMatch(p.Value.Text, $@"^extractvalue %dx\.types\.ResRet\.f32 {Regex.Escape(name)}, [0-2]$")).Select(p => p.Key).ToArray();
            var scales = channels.SelectMany(channel => instructions.Values.Select(i => Regex.Match(i.Text, $"^fmul(?: fast)? float ({Name}), ({Name})$"))
                .Where(m => m.Success && (m.Groups[1].Value == channel || m.Groups[2].Value == channel))
                .Select(m => m.Groups[1].Value == channel ? m.Groups[2].Value : m.Groups[1].Value)).ToArray();
            if (channels.Length == 3 && scales.Length == 3 && scales.Distinct().Count() == 1 && instructions.TryGetValue(scales[0], out var scale)
                && Regex.IsMatch(scale.Text, @"^extractvalue %dx\.types\.CBufRet\.f32 ") && sample.Line < firstLabel) scenes.Add(name);
        }
        if (scenes.Count != 1) throw new InvalidDataException("Display stage does not expose exactly one scene texture");
        var stored = new Regex($@"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 {target}, i32 0, i8 (?<column>[0-2]), float (?<value>\S+)\)$");
        var stores = new Dictionary<int, int>();
        for (int i = start + 1; i < end; i++)
        {
            var store = stored.Match(Code(i));
            if (store.Success) stores[store.Groups["column"].Value[0] - '0'] = i;
        }
        int lastLabel = Enumerable.Range(start + 1, end - start - 1).LastOrDefault(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:"), start);
        if (stores.Count != 3 || stores.Values.Any(line => line < lastLabel)) throw new InvalidDataException("Display stage does not write its colour in its final block");
        string Float(float value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
        var body = new List<string>();
        for (int c = 0; c < 3; c++)
        {
            body.Add($"  %aog_scene{c} = extractvalue %dx.types.ResRet.f32 {scenes[0]}, {c}");
            body.Add($"  %aog_exposed{c} = fmul fast float %aog_scene{c}, {Float(exposure)}");
            body.Add($"  %aog_clamped{c} = call float @dx.op.unary.f32(i32 7, float %aog_exposed{c})");
            body.Add($"  %aog_log{c} = call float @dx.op.unary.f32(i32 23, float %aog_clamped{c})");
            body.Add($"  %aog_gamma{c} = fmul fast float %aog_log{c}, {Float(1 / 2.2f)}");
            body.Add($"  %aog_shown{c} = call float @dx.op.unary.f32(i32 21, float %aog_gamma{c})");
            lines[stores[c]] = $"  call void @dx.op.storeOutput.f32(i32 5, i32 {target}, i32 0, i8 {c}, float %aog_shown{c})";
        }
        lines.InsertRange(stores.Values.Min(), body);
        if (!lines.Any(l => l.StartsWith("declare ", StringComparison.Ordinal) && l.Contains("@dx.op.unary.f32(", StringComparison.Ordinal)))
            lines.Add("declare float @dx.op.unary.f32(i32, float)");
        return string.Join('\n', lines) + "\n";
    }
}
