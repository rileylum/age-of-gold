using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

/// <summary>Rewrites the installed host stage that composes a frame from accumulated diffuse, specular and emitted light.</summary>
internal static class CompositeShader
{
    /// <summary>
    /// A build's scenery carries the source's lighting as emitted light. Where that light is marked by a weight below
    /// nothing, the frame is the emitted light alone, without the host's diffuse or specular light and without glow;
    /// everywhere else the host composes as it did.
    /// </summary>
    internal static string KeepSourceLight(string module)
    {
        if (module.Contains("%aog_", StringComparison.Ordinal)) throw new InvalidDataException("Composition stage is already rewritten");
        var signatures = new ShaderSignatures(module, "ps");
        int scene = signatures.Find(true, "SV_Target", 0).Id, glow = signatures.Find(true, "SV_Target", 1).Id;
        var lines = signatures.Lines.ToList();
        int start = lines.FindIndex(l => l == $"define void @{signatures.Entry}() {{");
        int end = lines.FindIndex(start + 1, l => l == "}");
        if (start < 0 || end < 0) throw new InvalidDataException("Expected one composition entry function");
        if (Enumerable.Range(start + 1, end - start - 1).Any(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:"))) throw new InvalidDataException("Composition stage is not one block");
        string Code(int line) => lines[line].Split(';')[0].Trim();
        const string name = @"%[\w.$-]+";
        var loads = new HashSet<string>(StringComparer.Ordinal);
        var parts = new Dictionary<(string Texel, int Part), string>();
        var stores = new Dictionary<(int Target, int Column), (int Line, string Value)>();
        for (int i = start + 1; i < end; i++)
        {
            var load = Regex.Match(Code(i), $@"^({name}) = call %dx\.types\.ResRet\.f32 @dx\.op\.textureLoad\.f32\(");
            if (load.Success) loads.Add(load.Groups[1].Value);
            var part = Regex.Match(Code(i), $@"^({name}) = extractvalue %dx\.types\.ResRet\.f32 ({name}), ([0-3])$");
            if (part.Success && !parts.TryAdd((part.Groups[2].Value, part.Groups[3].Value[0] - '0'), part.Groups[1].Value)) throw new InvalidDataException("Composition stage reads a texel's value twice");
            var store = Regex.Match(Code(i), @"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 (\d+), i32 0, i8 ([0-2]), float (\S+)\)$");
            if (store.Success && !stores.TryAdd((int.Parse(store.Groups[1].Value), store.Groups[2].Value[0] - '0'), (i, store.Groups[3].Value))) throw new InvalidDataException("Composition stage writes a target twice");
        }
        // The emitted light is the one loaded texel whose fourth value, its weight for glow, is read.
        var weighted = parts.Keys.Where(k => k.Part == 3 && loads.Contains(k.Texel)).Select(k => k.Texel).ToArray();
        if (weighted.Length != 1 || Enumerable.Range(0, 3).Any(c => !parts.ContainsKey((weighted[0], c)))) throw new InvalidDataException("Composition stage does not read exactly one emitted light with its weight");
        if (Enumerable.Range(0, 3).Any(c => !stores.ContainsKey((scene, c)) || !stores.ContainsKey((glow, c)))) throw new InvalidDataException("Composition stage does not write its scene and glow");
        // Each choice is made just above the store it feeds, where the value the host worked out is in hand; the mark is
        // read above the first of them.
        var inserts = new List<(int Line, string Text)>();
        for (int c = 0; c < 3; c++)
        {
            inserts.Add((stores[(scene, c)].Line, $"  %aog_scene{c} = select i1 %aog_marked, float {parts[(weighted[0], c)]}, float {stores[(scene, c)].Value}"));
            inserts.Add((stores[(glow, c)].Line, $"  %aog_glow{c} = select i1 %aog_marked, float 0.000000e+00, float {stores[(glow, c)].Value}"));
            lines[stores[(scene, c)].Line] = $"  call void @dx.op.storeOutput.f32(i32 5, i32 {scene}, i32 0, i8 {c}, float %aog_scene{c})";
            lines[stores[(glow, c)].Line] = $"  call void @dx.op.storeOutput.f32(i32 5, i32 {glow}, i32 0, i8 {c}, float %aog_glow{c})";
        }
        int first = inserts.Min(i => i.Line);
        if (Enumerable.Range(0, 4).Any(part => !parts.TryGetValue((weighted[0], part), out var read) || Array.FindIndex(lines.ToArray(), l => l.Split(';')[0].Trim().StartsWith(read + " = ", StringComparison.Ordinal)) >= first))
            throw new InvalidDataException("Composition stage reads its emitted light after it starts writing");
        foreach (var (line, text) in inserts.OrderByDescending(i => i.Line)) lines.Insert(line, text);
        lines.Insert(first, $"  %aog_marked = fcmp fast olt float {parts[(weighted[0], 3)]}, 0.000000e+00");
        return string.Join('\n', lines) + "\n";
    }
}
