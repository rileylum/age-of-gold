using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Params;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class LayerMaterialInstallCheck
{
    internal static void Run(string output)
    {
        ShaderMetadataInstallCheck.Run(output);
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        var archive = new HostArchive(host);
        byte[] Input(string name) => File.ReadAllBytes(Path.Combine(output, name));
        var definition = Input("template.matbin"); var donorDefinition = Input("donor.matbin");
        var metadata = Input("template.metaparam"); var donorMetadata = Input("donor.metaparam");
        using var scene = JsonDocument.Parse(SceneReader.Read(source));
        var lighting = SourceLightingReader.Read(source).Scenery.ToDictionary(r => r.Id);
        var selected = scene.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => (Name: m.GetProperty("name").GetString()!, Surfaces: m.GetProperty("surfaces").EnumerateArray()
                .Where(s => s.GetProperty("material").GetProperty("definition").GetString() == "M[DB][M]").ToArray()))
            .Where(m => m.Surfaces.Length != 0).ToArray();
        var environments = selected.ToDictionary(m => m.Name, m =>
        {
            var row = scene.RootElement.GetProperty("placements").EnumerateArray().Where(p => p.GetProperty("model").GetString() == m.Name)
                .Select(p => p.GetProperty("lighting").GetInt32()).Distinct().Single();
            return $"EnvDif_m10_{lighting[row].DiffuseEnvironment.Textures.Single():D3}";
        });
        var required = selected.SelectMany(m => m.Surfaces).SelectMany(s => s.GetProperty("material").GetProperty("textures").EnumerateArray())
            .Select(t => t.GetProperty("texture").GetString()!).Where(n => n != "").Concat(environments.Values).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var inputs = TextureReader.Read(source, required).ToDictionary(t => t.Name, t => t.Bytes, StringComparer.Ordinal);
        var names = required.Select((name, i) => (name, target: $"AOG_TEST_{i:D4}")).ToDictionary(t => t.name, t => t.target + ".tif", StringComparer.Ordinal);
        var environmentBinder = BND4.Read(archive.Read("map/m10/m10_01_00_00/m10_01_00_00_envmap_00_high_00.tpfbnd.dcx"));
        var template = environmentBinder.Files.Select(f => DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes)
            .First(b => b.Length > 36 && b[25] == 1 && new DDS(TextureReader.ReadPackage(b).First().Bytes).GetDXGIFormat() == DDS.DXGI_FORMAT.BC6H_UF16);
        var writer = new TextureWriter(template);
        var textures = new List<object>();
        foreach (var name in required)
        {
            var raw = inputs[name]; var dds = new DDS(raw);
            var first = writer.Write(names[name][..^4], raw, false);
            if (!first.SequenceEqual(writer.Write(names[name][..^4], raw, false))) throw new Exception("Bound texture rebuild differs");
            var back = TextureReader.ReadPackage(first).Single(); var header = new DDS(back.Bytes);
            if (back.Name + ".tif" != names[name] || header.dwWidth != dds.dwWidth || header.dwHeight != dds.dwHeight
                || header.dwCaps2 != dds.dwCaps2 || header.dwMipMapCount != dds.dwMipMapCount
                || !back.Bytes.AsSpan(header.DataOffset).SequenceEqual(raw.AsSpan(dds.DataOffset))) throw new Exception("Bound texture mip payload changed");
            if (environments.Values.Contains(name)) File.WriteAllBytes(Path.Combine(output, "bound-environment.tpf"), first);
            textures.Add(new { source = name, target = names[name], dds.dwWidth, dds.dwMipMapCount, input = Hash(raw), output = Hash(first) });
        }
        byte[] Model(string path) => BND4.Read(archive.Read(path)).Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal)).Bytes;
        var modelTemplate = Model("map/m10/m10_01_00_00/m10_01_00_00_000000.mapbnd.dcx");
        var layerTemplate = Model("map/m14/m14_00_00_00/m14_00_00_00_000001.mapbnd.dcx");
        var parsed = FLVER2.Read(layerTemplate);
        int exemplar = parsed.Meshes.FindIndex(m => parsed.Materials[m.MaterialIndex].Textures.Any(t => t.ParamName.StartsWith("M_AMSN_V_Mb2__", StringComparison.Ordinal)));
        var records = new List<object>(); int total = 0;
        foreach (var model in selected)
        {
            var surfaces = model.Surfaces.Select(s => LayerGeometry.Pack(LayerGeometry.Read(s))).ToArray();
            var bindings = model.Surfaces.Select(s => MaterialWriter.BindLayers(definition, metadata, donorDefinition, donorMetadata,
                LayerMaterial.Read(s.GetProperty("material")), names, names[environments[model.Name]])).ToArray();
            var first = LayerModelWriter.Write(modelTemplate, layerTemplate, exemplar, surfaces, bindings);
            if (!first.SequenceEqual(LayerModelWriter.Write(modelTemplate, layerTemplate, exemplar, surfaces, bindings))) throw new Exception("Bound layered model rebuild differs");
            var read = FLVER2.Read(first); LayerModelCheck.Check(read, surfaces);
            for (int i = 0; i < bindings.Length; i++)
            {
                var bound = bindings[i]; var material = read.Materials[read.Meshes[i].MaterialIndex];
                if (material.Index != i || material.GXIndex != 0 || material.Textures.Count != 7
                    || JsonSerializer.Serialize(material.Textures) != JsonSerializer.Serialize(bound.Textures)
                    || !MATBIN.Read(bound.Definition).Samplers.Select(s => s.Type).Order(StringComparer.Ordinal)
                        .SequenceEqual(material.Textures.Select(t => t.ParamName).Order(StringComparer.Ordinal)))
                    throw new Exception("Bound model, material and shader texture tables disagree");
                records.Add(new { model = model.Name, surface = model.Surfaces[i].GetProperty("index").GetInt32(), environment = environments[model.Name],
                    bindings = material.Textures.Select(t => new { t.ParamName, t.Path }), material = Hash(bound.Definition), metadata = Hash(bound.Metadata), geometry = Hash(first) });
            }
            File.WriteAllBytes(Path.Combine(output, "bound-layer.flver"), first);
            File.WriteAllBytes(Path.Combine(output, "bound.matbin"), bindings[0].Definition);
            total += surfaces.Length;
        }
        if (total != 85) throw new Exception("Bound ground surface inventory differs");
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
                if (Hash(bytes) != input.Value) throw new Exception("Installed bound material input changed");
            }
        File.WriteAllText(Path.Combine(output, "bindings-check.json"), JsonSerializer.Serialize(new { surfaces = total, textures, records,
            source = source.Fingerprints, host = host.Fingerprints, limits = "Texture binding and geometry readback; pixel shader translation and complete playable build remain unfinished" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {total} ground surfaces bind {required.Length} installed textures with complete mip payloads, repeated model bytes and unchanged installs");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
