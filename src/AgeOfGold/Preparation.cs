using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AgeOfGold.Archives;
using AgeOfGold.Scene;
using AgeOfGold.Collision;
using AgeOfGold.Params;
using AgeOfGold.Shaders;

namespace AgeOfGold;

internal static class Preparation
{
    internal static void Run(string sourcePath, string hostPath, string? requestPath, string outputPath)
    {
        var source = new Install(sourcePath, "DarkSoulsRemastered.exe");
        var host = new Install(hostPath, "eldenring.exe");
        string output = Install.Canonical(outputPath);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output already exists; use a fresh directory");
        if (new[] { source.Root, host.Root }.Any(root => Install.Within(output, root) || Install.Within(root, output)))
            throw new IOException("Output must be separate from both installs");
        var (sourceFiles, hostFiles) = requestPath is null ? (Array.Empty<string>(), new[] {
            "map/mapstudio/m10_01_00_00.msb.dcx", "material/allmaterial.matbinbnd.dcx",
            "map/m10/m10_01_00_00/m10_01_00_00_000000.mapbnd.dcx",
        }) : ReadRequest(requestPath);
        var archive = new HostArchive(host);
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".aog-preparing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var files = new List<object>();
            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
                files.Add(new { path = relative, size = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            }
            foreach (var path in sourceFiles) Write("source/" + path, source.Read(path));
            foreach (var path in hostFiles) Write("host/" + path, archive.Read(path));
            if (requestPath is null)
            {
                var regulation = Regulation.Read(host.Read("eldenring.exe"), host.Read("regulation.bin"));
                Write("host/regulation.bnd", regulation.Tables.Write(new SoulsFormats.DCX.NoCompressionInfo()));
                Write("collision.json", SourceCollisionReader.ReadFirelink(source));
                Write("lighting.json", JsonSerializer.SerializeToUtf8Bytes(SourceLightingReader.Read(source),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                Write("renderer.json", JsonSerializer.SerializeToUtf8Bytes(SourceRendererSettings.Read(source),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                var bytes = SceneReader.Read(source);
                Write("scene.json", bytes);
                using var scene = JsonDocument.Parse(bytes);
                var textures = scene.RootElement.GetProperty("models").EnumerateArray()
                    .SelectMany(m => m.GetProperty("surfaces").EnumerateArray())
                    .SelectMany(s => s.GetProperty("material").GetProperty("textures").EnumerateArray())
                    .Select(t => t.GetProperty("texture").GetString()!).Where(t => t != "");
                foreach (var texture in TextureReader.Read(source, textures))
                    Write("textures/" + texture.Name + ".dds", texture.Bytes);
            }
            var assemblies = new[] { Assembly.GetExecutingAssembly(), typeof(SoulsFormats.FLVER2).Assembly,
                typeof(HKLib.hk2018.IHavokObject).Assembly, typeof(HKLib.Reflection.hk2018.HavokType).Assembly,
                typeof(HKLib.Serialization.hk2018.Binary.HavokBinarySerializer).Assembly };
            var tool = assemblies.ToDictionary(a => a.GetName().Name!, a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))));
            var manifest = new
            {
                format = 1, kind = "prepared-install-inputs", tool,
                inputs = new { source = source.Fingerprints, host = host.Fingerprints }, files,
            };
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Directory.Move(staging, output);
            Console.WriteLine($"Prepared {files.Count} install inputs. This directory is not a playable build.");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static (string[], string[]) ReadRequest(string requestPath)
    {
        using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath));
        if (request.RootElement.ValueKind != JsonValueKind.Object
            || !request.RootElement.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "host", "source" }))
            throw new InvalidDataException("Request must contain only source and host file lists");
        string[] Paths(string game)
        {
            var list = request.RootElement.GetProperty(game);
            if (list.ValueKind != JsonValueKind.Array || list.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String))
                throw new InvalidDataException($"Request {game} must be a list of relative paths");
            var paths = list.EnumerateArray().Select(v => Install.Relative(v.GetString()!))
                .Select(p => game == "host" ? p.ToLowerInvariant() : p).Order(StringComparer.Ordinal).ToArray();
            if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
                throw new InvalidDataException($"Request {game} contains duplicate paths");
            return paths;
        }
        return (Paths("source"), Paths("host"));
    }
}
