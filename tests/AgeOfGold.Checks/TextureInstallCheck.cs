using System.Security.Cryptography;
using System.Text.Json;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class TextureInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || Directory.Exists(output) || File.Exists(output)
            || new[] { source.Root, host.Root }.Any(root => Install.Within(output, root) || Install.Within(root, output)))
            throw new IOException($"Texture check output must be fresh, separate from installs and under {Path.GetTempPath()}");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        var model = FLVER2.Read(BND4.Read(archive.Read("map/m10/m10_01_00_00/m10_01_00_00_000000.mapbnd.dcx")).Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal)).Bytes);
        var materials = BND4.Read(archive.Read("material/allmaterial.matbinbnd.dcx"));
        var material = MATBIN.Read(materials.Files.Single(f => Stem(f.Name) == Stem(model.Materials[0].MTD)).Bytes);
        var texture = Stem(material.Samplers.First(s => s.Type.Contains("AlbedoMap", StringComparison.Ordinal)).Path);
        string stem = texture[..texture.LastIndexOf('_')].ToLowerInvariant();
        var template = archive.Read($"asset/aet/{stem[..6]}/{stem}.tpf.dcx");
        var writer = new TextureWriter(template);
        using var scene = JsonDocument.Parse(SceneReader.Read(source));
        var names = scene.RootElement.GetProperty("models").EnumerateArray().SelectMany(m => m.GetProperty("surfaces").EnumerateArray())
            .SelectMany(s => s.GetProperty("material").GetProperty("textures").EnumerateArray())
            .Select(t => t.GetProperty("texture").GetString()!).Where(n => n != "");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "template.tpf.dcx"), template);
        var report = new List<object>();
        int sourceCount = 0;
        foreach (var (name, bytes) in TextureReader.Read(source, names))
        {
            var dds = new DDS(bytes);
            foreach (bool srgb in dds.GetDXGIFormat() == DDS.DXGI_FORMAT.BC5_UNORM ? new[] { false } : new[] { false, true })
            {
                string target = $"AOG_{report.Count:D4}";
                var result = writer.Write(target, bytes, srgb);
                if (!result.SequenceEqual(writer.Write(target, bytes, srgb))) throw new Exception("Installed texture package repeat differs");
                var readback = TextureReader.ReadPackage(result).Single();
                var actual = new DDS(readback.Bytes);
                var expectedFormat = (dds.GetDXGIFormat(), srgb) switch {
                    (DDS.DXGI_FORMAT.BC1_UNORM, true) => DDS.DXGI_FORMAT.BC1_UNORM_SRGB,
                    (DDS.DXGI_FORMAT.BC7_UNORM, true) => DDS.DXGI_FORMAT.BC7_UNORM_SRGB,
                    _ => dds.GetDXGIFormat(),
                };
                if (readback.Name != target || actual.dwWidth != dds.dwWidth || actual.dwHeight != dds.dwHeight
                    || actual.dwMipMapCount != dds.dwMipMapCount || actual.GetDXGIFormat() != expectedFormat
                    || !readback.Bytes.AsSpan(actual.DataOffset).SequenceEqual(bytes.AsSpan(dds.DataOffset)))
                    throw new Exception("Installed texture payload, name, dimensions, mips or sampling changed");
                string relative = target + ".tpf.dcx";
                File.WriteAllBytes(Path.Combine(output, relative), result);
                report.Add(new { source = name, path = relative, name = target, width = dds.dwWidth, height = dds.dwHeight,
                    mips = dds.dwMipMapCount, format = (int)expectedFormat, sha256 = Convert.ToHexString(SHA256.HashData(result)),
                    payloadSha256 = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(dds.DataOffset))) });
            }
            sourceCount++;
        }
        if (sourceCount != 208) throw new Exception("Installed source texture scope differs from checked scene");
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
            {
                var range = System.Text.RegularExpressions.Regex.Match(input.Key, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var file = File.OpenRead(install.Locate(range.Groups[1].Value));
                    file.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)];
                    file.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(install.Locate(input.Key));
                if (Convert.ToHexString(SHA256.HashData(bytes)) != input.Value) throw new Exception("Install input changed");
            }
        File.WriteAllText(Path.Combine(output, "check.json"), JsonSerializer.Serialize(new { sourceCount, files = report,
            inputs = new { source = source.Fingerprints, host = host.Fingerprints } }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {sourceCount} installed surface textures, {report.Count} host packages; exact repeats, DDS readback, preserved mip payloads and unchanged installs");
    }

    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\','/'));
    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
