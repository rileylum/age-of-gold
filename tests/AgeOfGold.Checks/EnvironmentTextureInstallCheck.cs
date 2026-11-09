using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class EnvironmentTextureInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || Directory.Exists(output) || File.Exists(output)
            || new[] { source.Root, host.Root }.Any(r => Install.Within(output, r) || Install.Within(r, output)))
            throw new ArgumentException("Fresh environment check output under system temp required");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        var binder = BND4.Read(archive.Read("map/m10/m10_01_00_00/m10_01_00_00_envmap_00_high_00.tpfbnd.dcx"));
        var candidates = binder.Files.Select(f => DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes)
            .Where(b => b.Length > 36 && b[25] == 1).ToArray();
        var template = candidates.FirstOrDefault(b => new DDS(TextureReader.ReadPackage(b).First().Bytes).GetDXGIFormat() == DDS.DXGI_FORMAT.BC6H_UF16)
            ?? throw new InvalidDataException("Installed HDR environment template missing");
        var writer = new TextureWriter(template);
        const string header = "map/m10/GI_EnvM_m10.tpfbhd", data = "map/m10/GI_EnvM_m10.tpfbdt";
        source.Fingerprint(header); source.Fingerprint(data);
        using var inputs = new BXF3Reader(source.Locate(header), source.Locate(data));
        var records = new List<object>();
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "template.tpf"), template);
        foreach (var file in inputs.Files)
            foreach (var (name, bytes) in TextureReader.ReadPackage(inputs.ReadFile(file)))
            {
                var dds = new DDS(bytes);
                string target = $"AOG_ENV_{records.Count:D4}";
                var first = writer.Write(target, bytes, false);
                if (!first.SequenceEqual(writer.Write(target, bytes, false))) throw new Exception("Installed cube rebuild differs");
                var payload = bytes.AsSpan(dds.DataOffset).ToArray();
                EnvironmentTextureCheck.Check(first, payload, dds.dwWidth, dds.dwMipMapCount, template[24]);
                var readback = TextureReader.ReadPackage(first).Single();
                if (readback.Name != target) throw new Exception("Cube lookup name changed");
                // Retain one diffuse and one specular representative, without growing a second environment library.
                if (records.Count < 2) File.WriteAllBytes(Path.Combine(output, target + ".tpf"), first);
                records.Add(new { source = name, name = target, dds.dwWidth, dds.dwMipMapCount,
                    output = Convert.ToHexString(SHA256.HashData(first)), payload = Convert.ToHexString(SHA256.HashData(payload)) });
            }
        if (records.Count == 0) throw new Exception("Installed source environment library was empty");
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
            {
                var range = Regex.Match(input.Key, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var stream = File.OpenRead(install.Locate(range.Groups[1].Value)); stream.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)]; stream.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(install.Locate(input.Key));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != input.Value) throw new Exception("Installed cube input changed");
            }
        File.WriteAllText(Path.Combine(output, "check.json"), JsonSerializer.Serialize(new { count = records.Count, records,
            inputs = new { source = source.Fingerprints, host = host.Fingerprints } }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {records.Count} installed HDR cubes, six faces and all mip levels retained, host type derived, double builds and unchanged installs");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
