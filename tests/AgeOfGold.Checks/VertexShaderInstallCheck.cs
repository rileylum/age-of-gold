using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Scene;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class VertexShaderInstallCheck
{
    internal static void Extract(string directory)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        Fresh(directory);
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        using var scene = JsonDocument.Parse(SceneReader.Read(source));
        int layers = scene.RootElement.GetProperty("models").EnumerateArray().SelectMany(m => m.GetProperty("surfaces").EnumerateArray())
            .Count(s => s.GetProperty("material").GetProperty("definition").GetString() == "M[DB][M]");
        if (layers != 85) throw new Exception("Installed source ground-family inventory changed");
        var archive = new HostArchive(host);
        var family = ShaderArchive.ReadFamily(archive.Read("shader/shaderbdle.shaderbdlebnd.dcx"), "M[AMSN_V][Mb2].shaderbdle");
        using var binder = new BND4Reader(family);
        var shaders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Directory.CreateDirectory(directory);
        foreach (var file in binder.Files)
        {
            string name = Path.GetFileName(file.Name.Replace('\\', '/'));
            if (!name.Contains("_Gbuf", StringComparison.Ordinal) || !name.EndsWith(".vpo", StringComparison.Ordinal)) continue;
            var bytes = ShaderArchive.Read(family, name);
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
            shaders.Add(name, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        if (shaders.Count == 0) throw new Exception("Installed ground vertex programs missing");
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
        File.WriteAllText(Path.Combine(directory, "inputs.json"), JsonSerializer.Serialize(new
            { source = source.Fingerprints, host = host.Fingerprints, layers, shaders }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {shaders.Count} ground vertex programs freshly extracted, {layers} source surfaces and unchanged installed inputs");
    }

    internal static void Check(string directory)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        if (!Install.Within(Install.Canonical(directory), Install.Canonical(Path.GetTempPath())))
            throw new ArgumentException("Prepared shader checks must remain under system temp");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "inputs.json")));
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var report = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in manifest.RootElement.GetProperty("shaders").EnumerateObject())
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, Install.Relative(input.Name)));
            if (Convert.ToHexString(SHA256.HashData(bytes)) != input.Value.GetString()) throw new Exception("Prepared vertex input changed");
            var text = compiler.Disassemble(bytes);
            var first = compiler.Assemble(VertexShader.AddSecondaryTangent(text), bytes);
            var second = compiler.Assemble(VertexShader.AddSecondaryTangent(text), bytes);
            if (!first.SequenceEqual(second)) throw new Exception("Installed vertex transformation does not repeat");
            var parsed = compiler.Disassemble(first);
            new ShaderSignatures(parsed).Require(false, "TANGENT", 2, 5);
            new ShaderSignatures(parsed).Require(true, "TANGENT", 2, 9);
            var output = Path.Combine(directory, input.Name + ".rebuilt");
            using (var stream = new FileStream(output, FileMode.CreateNew)) stream.Write(first);
            if (!first.SequenceEqual(File.ReadAllBytes(output))) throw new Exception("Vertex output readback differs");
            report.Add(input.Name, Convert.ToHexString(SHA256.HashData(first)));
        }
        File.WriteAllText(Path.Combine(directory, "check.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {report.Count} freshly installed vertex shaders rewritten by semantics, rebuilt twice and validated/read back");
    }

    private static void Fresh(string directory)
    {
        if (Directory.Exists(directory) || !Install.Within(Install.Canonical(directory), Install.Canonical(Path.GetTempPath())))
            throw new ArgumentException("Use fresh output under system temp");
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
