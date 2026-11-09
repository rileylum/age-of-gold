using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class LayerModelInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        byte[] Model(string path) => BND4.Read(archive.Read(path)).Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal)).Bytes;
        var template = Model("map/m10/m10_01_00_00/m10_01_00_00_000000.mapbnd.dcx");
        var layer = Model("map/m14/m14_00_00_00/m14_00_00_00_000001.mapbnd.dcx");
        var parsed = FLVER2.Read(layer);
        var mesh = parsed.Meshes.FindIndex(m => parsed.Materials[m.MaterialIndex].Textures.Any(t => t.ParamName.StartsWith("M_AMSN_V_Mb2__", StringComparison.Ordinal)));
        if (mesh < 0) throw new Exception("Installed layer template not found");
        using var scene = JsonDocument.Parse(SceneReader.Read(source));
        int surfaces = 0, vertices = 0, triangles = 0;
        foreach (var model in scene.RootElement.GetProperty("models").EnumerateArray())
        {
            var selected = model.GetProperty("surfaces").EnumerateArray()
                .Where(s => s.GetProperty("material").GetProperty("definition").GetString() == "M[DB][M]")
                .Select(s => LayerGeometry.Pack(LayerGeometry.Read(s))).ToArray();
            if (selected.Length == 0) continue;
            var first = LayerModelWriter.Write(template, layer, mesh, selected);
            var second = LayerModelWriter.Write(template, layer, mesh, selected);
            if (!first.SequenceEqual(second)) throw new Exception("Independent layered model builds differ");
            var path = Path.Combine(output, "layered.flver");
            File.WriteAllBytes(path, first);
            LayerModelCheck.Check(FLVER2.Read(File.ReadAllBytes(path)), selected);
            surfaces += selected.Length; vertices += selected.Sum(s => s.Vertices.Length); triangles += selected.Sum(s => s.Indices.Length / 3);
        }
        if (surfaces != 85) throw new Exception("Source layered surface inventory changed");
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
            {
                var range = Regex.Match(input.Key, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var file = File.OpenRead(install.Locate(range.Groups[1].Value));
                    file.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)]; file.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(install.Locate(input.Key));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != input.Value) throw new Exception("Install input changed");
            }
        Console.WriteLine($"PASS: {surfaces} installed layered surfaces, {vertices} vertices, {triangles} triangles; host readback, repeat bytes and unchanged installs");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
