using System.Text.Json;
using AgeOfGold;
using AgeOfGold.Shaders;
using SoulsFormats;

/// <summary>Compares a maintained build with a private accepted build. The reference is evidence only, never a build input.</summary>
internal static class FirelinkReferenceCheck
{
    internal static void Run(string build, string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        string reference = Environment.GetEnvironmentVariable("AOG_BUILD_REFERENCE") ?? throw new ArgumentException("Set AOG_BUILD_REFERENCE to the private accepted mod directory");
        Oodle.SearchPaths.Add(Environment.GetEnvironmentVariable("AOG_ELDEN_RING") ?? throw new ArgumentException("Set AOG_ELDEN_RING"));
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || File.Exists(output)) throw new ArgumentException("Fresh report under system temp required");
        string[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        var ours = Files(build); var theirs = Files(reference);
        var identical = ours.Intersect(theirs).Where(f => File.ReadAllBytes(Path.Combine(build, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(reference, f)))).ToArray();
        const string map = "map/mapstudio/m10_01_00_00.msb.dcx";
        var a = MSBE.Read(Path.Combine(build, map)); var b = MSBE.Read(Path.Combine(reference, map));
        object Parts(MSBE m) => new
        {
            player = m.Parts.Players.Select(p => new { p.Name, p.Position, p.Rotation }),
            enabled = m.Parts.GetEntries().Where(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable)
                .GroupBy(p => p.GetType().Name).ToDictionary(g => g.Key, g => g.Count()),
            firstScenery = m.Parts.MapPieces.Where(p => p.ModelName.StartsWith("m81", StringComparison.Ordinal)).Take(2).Select(p => new { p.Name, p.ModelName, p.Position, p.Rotation }),
            regions = m.Regions.GetEntries().GroupBy(p => p.GetType().Name).ToDictionary(g => g.Key, g => g.Count()),
            events = m.Events.GetEntries().GroupBy(p => p.GetType().Name).ToDictionary(g => g.Key, g => g.Count()),
        };
        // The accepted ground program was hand-patched; the maintained one must compute the same values.
        var compiler = new ShaderCompiler(Environment.GetEnvironmentVariable("AOG_DXC") ?? throw new ArgumentException("Set AOG_DXC"),
            Environment.GetEnvironmentVariable("AOG_DXC_ASSEMBLER") ?? throw new ArgumentException("Set AOG_DXC_ASSEMBLER"));
        const string shaders = "shader/shaderbdle.shaderbdlebnd.dcx";
        string Program(string root, string family) => compiler.Disassemble(
            ShaderArchive.Read(File.ReadAllBytes(Path.Combine(root, shaders)), family + ".shaderbdle", family + "_Gbuf.ppo"));
        string maintained = Program(build, "aog_ground_20"), accepted = Program(reference, "aog_source_environment_trial");
        double worst = 0; var random = new Random(20261114);
        for (int trial = 0; trial < 64; trial++)
        {
            var inputs = new Dictionary<(string, int), float[]>();
            float[] Values(string name, int index)
            {
                if (!inputs.TryGetValue((name, index), out var values))
                    inputs.Add((name, index), values = Enumerable.Range(0, 4).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray());
                return values;
            }
            Values("TEXCOORD", 0)[3] = (float)random.NextDouble();
            for (int i = 0; i < 3; i++) Values("TEXCOORD", 0)[i] = (float)random.NextDouble();
            bool front = trial % 2 == 0; int seed = trial;
            Dictionary<(int, int), float> Evaluate(string module)
            {
                var signatures = new ShaderSignatures(module, "ps");
                var evaluator = new ShaderEvaluator
                {
                    Constants = (register, index) => Enumerable.Range(0, 4).Select(c => 0.25f + ((register * 31 + index * 7 + c * 3 + seed) % 17) / 16f).ToArray(),
                    Sample = (slot, at, _) => Enumerable.Range(0, 4).Select(c => 0.5f + 0.4f * MathF.Sin(slot + c + at.Select((v, i) => v * (i + 1.5f)).Sum())).ToArray(),
                };
                evaluator.Input = (id, column) =>
                {
                    var member = Enumerable.Range(0, 16).SelectMany(i => new[] { "SV_Position", "NORMAL", "TANGENT", "TEXCOORD", "SV_IsFrontFace" }.Select(n => (n, i)))
                        .Select(k => { try { return signatures.Find(false, k.n, k.i); } catch (InvalidDataException) { return null; } }).First(m => m is not null && m.Id == id)!;
                    if (member.Name == "SV_IsFrontFace") return front ? -1 : 0;
                    if (member.Name == "TEXCOORD" && member.Index == 15) return 0f;
                    return Values(member.Name, member.Index)[column];
                };
                evaluator.Run(module);
                var targets = Enumerable.Range(0, 5).ToDictionary(i => signatures.Find(true, "SV_Target", i).Id);
                return evaluator.Outputs.ToDictionary(p => (targets[p.Key.Id], p.Key.Column), p => p.Value);
            }
            var mine = Evaluate(maintained); var expected = Evaluate(accepted);
            if (!mine.Keys.Order().SequenceEqual(expected.Keys.Order())) throw new Exception("Maintained ground program writes different targets");
            foreach (var (key, value) in expected)
            {
                double error = Math.Abs(value - mine[key]) / Math.Max(1, Math.Abs(value));
                if (!(error <= 2e-4)) throw new Exception($"Maintained ground program differs at target {key}: {mine[key]} != {value}");
                worst = Math.Max(worst, error);
            }
        }
        Console.WriteLine($"Maintained ground pixel program matches the accepted program on 64 authored inputs; worst relative error {worst:E2}");
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            identical, onlyBuild = ours.Except(theirs).Count(), onlyReference = theirs.Except(ours).ToArray(),
            differing = ours.Intersect(theirs).Except(identical).ToArray(), build = Parts(a), reference = Parts(b),
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"Reference comparison: {identical.Length} identical, {ours.Intersect(theirs).Count() - identical.Length} differing, {theirs.Except(ours).Count()} only in the reference");
    }
}
