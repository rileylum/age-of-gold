using System.Text.Json;
using AgeOfGold;
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
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            identical, onlyBuild = ours.Except(theirs).Count(), onlyReference = theirs.Except(ours).ToArray(),
            differing = ours.Intersect(theirs).Except(identical).ToArray(), build = Parts(a), reference = Parts(b),
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Console.WriteLine($"Reference comparison: {identical.Length} identical, {ours.Intersect(theirs).Count() - identical.Length} differing, {theirs.Except(ours).Count()} only in the reference");
    }
}
