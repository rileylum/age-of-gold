using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold.Archives;
using AgeOfGold.Collision;
using AgeOfGold.Maps;
using AgeOfGold.Params;
using AgeOfGold.Scene;
using AgeOfGold.Shaders;
using SoulsFormats;

namespace AgeOfGold.Building;

/// <summary>Reads both installs and writes the Firelink build: a mod directory, its mod loader profile and a manifest.</summary>
internal static class FirelinkBuild
{
    private const string Map = "m10_01_00_00", HostFamily = "M[AMSN_V][Mb2]", DonorFamily = "M[AMSN][Mb2]", GroundFamily = "aog_ground";

    internal static void Run(string sourcePath, string hostPath, string outputPath, ShaderCompiler compiler)
    {
        var source = new Install(sourcePath, "DarkSoulsRemastered.exe");
        var host = new Install(hostPath, "eldenring.exe");
        string output = Install.Canonical(outputPath);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output already exists; use a fresh directory");
        if (new[] { source.Root, host.Root }.Any(root => Install.Within(output, root) || Install.Within(root, output)))
            throw new IOException("Output must be separate from both installs");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var unbuilt = new SortedDictionary<string, int>(StringComparer.Ordinal);
        // A new identity must never shadow an installed host file.
        void Add(string path, byte[] bytes)
        {
            if (archive.Contains(path)) throw new InvalidDataException("Build identity is occupied in the host install: " + path);
            files.Add("mod/" + path, bytes);
        }

        using var scene = JsonDocument.Parse(SceneReader.Read(source));
        var collision = SourceCollisionReader.ReadScene(source);
        var lighting = SourceLightingReader.Read(source).Scenery.ToDictionary(r => r.Id);
        var placements = scene.RootElement.GetProperty("placements").EnumerateArray().ToArray();

        // lazy: only the two-layer ground family is built; every other surface is counted in the manifest until its family lands.
        var models = new List<(string Name, JsonElement[] Surfaces)>();
        foreach (var model in scene.RootElement.GetProperty("models").EnumerateArray())
        {
            var ground = new List<JsonElement>();
            foreach (var surface in model.GetProperty("surfaces").EnumerateArray())
            {
                string definition = surface.GetProperty("material").GetProperty("definition").GetString()!;
                if (definition == "M[DB][M]") ground.Add(surface);
                else unbuilt[definition] = unbuilt.GetValueOrDefault(definition) + 1;
            }
            if (ground.Count != 0) models.Add((model.GetProperty("name").GetString()!, ground.ToArray()));
        }

        var layout = MSBE.Read(archive.Read($"map/mapstudio/{Map}.msb.dcx"));
        FirelinkMapWriter.IsolateWorld(layout);
        var terrain = FirelinkMapWriter.AddTerrain(layout, collision);
        foreach (string detail in new[] { "h", "l" })
        {
            string stem = $"map/m10/{Map}/{detail}{Map[1..]}";
            var pair = HostCollisionWriter.Write(archive.Read(stem + ".hkxbhd"), archive.Read(stem + ".hkxbdt"), detail, detail == "h" ? terrain.High : terrain.Low);
            files.Add("mod/" + stem + ".hkxbhd", pair.Header); files.Add("mod/" + stem + ".hkxbdt", pair.Data);
        }
        static Vector3 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        var built = models.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var scenery = FirelinkMapWriter.AddScenery(layout, placements.Where(p => built.Contains(p.GetProperty("model").GetString()!))
            .Select(p => new FirelinkMapWriter.SceneryPlacement(p.GetProperty("name").GetString()!, p.GetProperty("model").GetString()!,
                V(p.GetProperty("position")), V(p.GetProperty("rotation")), V(p.GetProperty("scale")))).ToArray(), terrain.Offset);
        files.Add($"mod/map/mapstudio/{Map}.msb.dcx", layout.Write());

        // Each model samples the diffuse environment of the one lighting row its placements select.
        var environments = models.ToDictionary(m => m.Name, m =>
        {
            var rows = placements.Where(p => p.GetProperty("model").GetString() == m.Name).Select(p => p.GetProperty("lighting").GetInt32()).Distinct().ToArray();
            if (rows.Length != 1) throw new InvalidDataException("Scenery model is placed under several lighting rows: " + m.Name);
            var textures = lighting[rows[0]].DiffuseEnvironment.Textures;
            if (textures.Length != 1) throw new InvalidDataException("Lighting row lacks one diffuse environment texture");
            return $"EnvDif_m10_{textures[0]:D3}";
        }, StringComparer.Ordinal);
        var required = models.SelectMany(m => m.Surfaces).SelectMany(s => s.GetProperty("material").GetProperty("textures").EnumerateArray())
            .Select(t => t.GetProperty("texture").GetString()!).Where(n => n != "").Concat(environments.Values)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var environmentBinder = BND4.Read(archive.Read($"map/m10/{Map}/{Map}_envmap_00_high_00.tpfbnd.dcx"));
        var textureTemplate = environmentBinder.Files.Select(f => DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes)
            .FirstOrDefault(b => b.Length > 36 && b[25] == 1 && new DDS(TextureReader.ReadPackage(b).First().Bytes).GetDXGIFormat() == DDS.DXGI_FORMAT.BC6H_UF16)
            ?? throw new InvalidDataException("Host install lacks an HDR cube texture template");
        var textureWriter = new TextureWriter(textureTemplate);
        var raw = TextureReader.Read(source, required).ToDictionary(t => t.Name, t => t.Bytes, StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (required.Length > 1000) throw new InvalidDataException("Texture namespace exhausted");
        for (int i = 0; i < required.Length; i++)
        {
            // lazy: one reserved namespace of a thousand textures; split across namespaces when a larger area needs more.
            string stem = $"aet950_{i:D3}", name = stem.ToUpperInvariant() + "_a";
            foreach (string suffix in new[] { "", "_l" })
                Add($"asset/aet/aet950/{stem}{suffix}.tpf.dcx", textureWriter.Write(name + suffix, raw[required[i]], false));
            names.Add(required[i], name + ".tif");
        }

        var shaders = archive.Read("shader/shaderbdle.shaderbdlebnd.dcx");
        byte[] Metadata(string family)
        {
            using var reader = new BND4Reader(ShaderArchive.ReadFamily(shaders, family + ".shaderbdle"));
            return reader.ReadFile(reader.Files.Single(f => f.Name.EndsWith(".metaparam", StringComparison.Ordinal)));
        }
        var definitions = BND4.Read(archive.Read("material/allmaterial.matbinbnd.dcx"));
        BinderFile Definition(string family) => definitions.Files.Single(f => f.Name.Replace('\\', '/')
            .EndsWith("/Map_Base/matxml/" + family + ".matbin", StringComparison.Ordinal));
        var hostDefinition = Definition(HostFamily);
        byte[] metadata = Metadata(HostFamily), donorMetadata = Metadata(DonorFamily), donorDefinition = Definition(DonorFamily).Bytes;
        byte[] Model(string path) => BND4.Read(archive.Read(path)).Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal)).Bytes;
        string modelTemplatePath = $"map/m10/{Map}/{Map}_000000.mapbnd.dcx";
        var modelTemplate = Model(modelTemplatePath);
        var layerTemplate = Model("map/m14/m14_00_00_00/m14_00_00_00_000001.mapbnd.dcx");
        var layerModel = FLVER2.Read(layerTemplate);
        int exemplar = layerModel.Meshes.FindIndex(m => layerModel.Materials[m.MaterialIndex].Textures.Any(t => t.ParamName.StartsWith("M_AMSN_V_Mb2__", StringComparison.Ordinal)));
        if (exemplar < 0) throw new InvalidDataException("Host install lacks the two-layer ground exemplar");

        byte[]? groundDefinition = null, groundMetadata = null;
        foreach (var model in models)
        {
            var surfaces = model.Surfaces.Select(s => LayerGeometry.Pack(LayerGeometry.Read(s))).ToArray();
            var bindings = model.Surfaces.Select(s =>
            {
                var bound = MaterialWriter.BindLayers(hostDefinition.Bytes, metadata, donorDefinition, donorMetadata,
                    LayerMaterial.Read(s.GetProperty("material")), names, names[environments[model.Name]]);
                var definition = MATBIN.Read(bound.Definition);
                definition.SourcePath = Rename(definition.SourcePath); definition.ShaderPath = Rename(definition.ShaderPath);
                return bound with { Definition = definition.Write() };
            }).ToArray();
            foreach (var bound in bindings)
            {
                groundDefinition ??= bound.Definition; groundMetadata ??= bound.Metadata;
                if (!bound.Definition.AsSpan().SequenceEqual(groundDefinition) || !bound.Metadata.AsSpan().SequenceEqual(groundMetadata))
                    throw new InvalidDataException("Ground surfaces need more than one material definition");
            }
            string digits = scenery.Models[model.Name];
            var binder = BND4.Read(archive.Read(modelTemplatePath));
            var file = binder.Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal));
            file.Bytes = LayerModelWriter.Write(modelTemplate, layerTemplate, exemplar, surfaces, bindings);
            file.Name = file.Name.Replace($"{Map}_000000", $"{Map}_{digits}");
            binder.Files = [file];
            Add($"map/m10/{Map}/{Map}_{digits}.mapbnd.dcx", binder.Write());
        }
        if (groundDefinition is null || groundMetadata is null) throw new InvalidDataException("Source area has no ground surfaces");
        string definitionName = Rename(hostDefinition.Name);
        if (definitions.Files.Any(f => f.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Ground material identity is occupied");
        definitions.Files.Add(new BinderFile(hostDefinition.Flags, definitions.Files.Max(f => f.ID) + 1, definitionName, groundDefinition));
        files.Add("mod/material/allmaterial.matbinbnd.dcx", definitions.Write());

        files.Add("mod/shader/shaderbdle.shaderbdlebnd.dcx", ShaderArchive.AddFamily(shaders, HostFamily, GroundFamily, (suffix, bytes) =>
        {
            if (suffix == ".metaparam") return groundMetadata;
            // The ground model carries a second tangent and both bitangents; only its G-buffer vertex stages forward them.
            if (Regex.IsMatch(suffix, @"^_Gbuf.*\.vpo$")) return compiler.Assemble(VertexShader.AddLayerFrames(compiler.Disassemble(bytes)), bytes);
            return bytes;
        }));

        files.Add("age-of-gold.me3", System.Text.Encoding.UTF8.GetBytes(
            "profileVersion = \"v1\"\nsavefile = \"AgeOfGold.sl2\"\nstart_online = false\n\n[[supports]]\ngame = \"eldenring\"\n\n[[packages]]\npath = 'mod'\n"));

        var assemblies = new[] { Assembly.GetExecutingAssembly(), typeof(FLVER2).Assembly, typeof(HKLib.hk2018.IHavokObject).Assembly,
            typeof(HKLib.Reflection.hk2018.HavokType).Assembly, typeof(HKLib.Serialization.hk2018.Binary.HavokBinarySerializer).Assembly };
        var manifest = new
        {
            format = 1, kind = "firelink-build",
            tool = assemblies.ToDictionary(a => a.GetName().Name!, a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location)))),
            shaderCompiler = ShaderCompiler.Version,
            inputs = new { source = source.Fingerprints, host = host.Fingerprints },
            scenery = new { models = scenery.Models, placements = scenery.Placements },
            unbuiltSurfaces = unbuilt,
            excludedCollision = terrain.Excluded.Select(p => new { p.Name, p.Role, p.Disabled }),
            files = files.Select(f => new { path = f.Key, size = f.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(f.Value)) }),
        };
        files.Add("manifest.json", System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n"));

        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".aog-building-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (relative, bytes) in files)
            {
                var path = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Directory.Move(staging, output);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        Console.WriteLine($"Built {files.Count} files. {unbuilt.Values.Sum()} source surfaces in {unbuilt.Count} material families are not built yet.");
    }

    private static string Rename(string path) => path.Contains(HostFamily, StringComparison.Ordinal)
        ? path.Replace(HostFamily, GroundFamily) : throw new InvalidDataException("Host material path lacks its family name");
}
