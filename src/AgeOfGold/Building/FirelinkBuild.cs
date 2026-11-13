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
    // The host's deferred targets as its installed opaque programs write them: encoded normal, surface colour, emitted light.
    // lazy: zero emission weight keeps the host from adding glow; the source local lights need the light-composition stage and its marker.
    private static readonly PixelShader.Targets HostTargets = new(0, 2, 4, 0);

    internal static void Run(string sourcePath, string hostPath, string outputPath, ShaderCompiler compiler, string toolchain)
    {
        var source = new Install(sourcePath, "DarkSoulsRemastered.exe");
        var host = new Install(hostPath, "eldenring.exe");
        string output = Install.Canonical(outputPath);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output already exists; use a fresh directory");
        if (new[] { source.Root, host.Root }.Any(root => Install.Within(output, root) || Install.Within(root, output)))
            throw new IOException("Output must be separate from both installs");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".aog-building-" + Guid.NewGuid().ToString("N"));
        try
        {
            Stage(source, host, archive, compiler, toolchain, new Output(staging));
            Directory.Move(staging, output);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    // Written files are released at once; only their fingerprints stay in memory.
    private sealed class Output(string root)
    {
        internal readonly SortedDictionary<string, (int Size, string Hash)> Files = new(StringComparer.Ordinal);
        internal void Add(string relative, byte[] bytes)
        {
            Files.Add(relative, (bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    private static void Stage(Install source, Install host, HostArchive archive, ShaderCompiler compiler, string toolchain, Output files)
    {
        var unbuilt = new SortedDictionary<string, int>(StringComparer.Ordinal);
        // A new identity must never shadow an installed host file.
        void Add(string path, byte[] bytes)
        {
            if (archive.Contains(path)) throw new InvalidDataException("Build identity is occupied in the host install: " + path);
            files.Add("mod/" + path, bytes);
        }

        var reader = new SceneReader(source);
        var collision = SourceCollisionReader.ReadScene(source);
        var lighting = SourceLightingReader.Read(source).Scenery.ToDictionary(r => r.Id);
        using var placementDocument = JsonDocument.Parse(reader.ReadPlacements());
        var placements = placementDocument.RootElement.EnumerateArray().ToArray();

        // lazy: only the opaque lit families are built; every other surface is counted in the manifest until its family lands.
        // First pass: classify each model's surfaces and collect texture names; geometry is read again when its model is built.
        var renderer = SourceRendererSettings.Read(source);
        var environmentSettings = SourceEnvironmentSettings.Read(source, toolchain);
        var models = new List<(string Name, Dictionary<int, (float R, float G, float B, bool Unlit, float FadeOffset, float FadeScale)> Surfaces, string[] Textures)>();
        foreach (string name in reader.Models)
        {
            using var model = JsonDocument.Parse(reader.ReadModel(name));
            var ground = new Dictionary<int, (float, float, float, bool, float, float)>(); var textures = new List<string>();
            foreach (var surface in model.RootElement.GetProperty("surfaces").EnumerateArray())
            {
                var material = surface.GetProperty("material");
                string definition = material.GetProperty("definition").GetString()!;
                if (LayerMaterial.Layers(material) == 0) { unbuilt[definition] = unbuilt.GetValueOrDefault(definition) + 1; continue; }
                var parameters = material.GetProperty("parameters");
                // The source applies its environment light only to materials in the renderer's environment mode; type zero is unlit.
                // Backdrop planes have no lighting type and are lit like the renderer's environment mode.
                int lightingType = parameters.TryGetProperty("g_LightingType", out var declared) ? declared.GetInt32() : environmentSettings.LightingType;
                var fade = LayerMaterial.ViewFade(material);
                if (lightingType != 0 && lightingType != environmentSettings.LightingType)
                    throw new InvalidDataException("Opaque material uses an unclassified lighting type: " + name);
                float power = parameters.GetProperty("g_DiffuseMapColorPower").GetSingle();
                var tint = parameters.GetProperty("g_DiffuseMapColor").EnumerateArray().Select(v => MathF.Pow(v.GetSingle() * power, 2.2f)).ToArray();
                if (tint.Length != 3) throw new InvalidDataException("Unsupported diffuse multiplier: " + name);
                ground.Add(surface.GetProperty("index").GetInt32(), (tint[0], tint[1], tint[2], lightingType == 0, fade?.Offset ?? 0, fade?.Scale ?? 0));
                var layers = LayerMaterial.Read(material);
                textures.AddRange(new[] { layers.First, layers.Second }.SelectMany(l => new[] { l.Colour, l.Normal, l.Packed }));
            }
            if (ground.Count != 0) models.Add((name, ground, textures.ToArray()));
        }

        var layout = MSBE.Read(archive.Read($"map/mapstudio/{Map}.msb.dcx"));
        FirelinkMapWriter.IsolateWorld(layout);
        var hostSpawn = layout.Parts.Players.Single(p => p.Name == "c0000_0000").Position;
        var terrain = FirelinkMapWriter.AddTerrain(layout, collision);
        FirelinkMapWriter.MoveEnvironment(layout, terrain.Spawn - hostSpawn, terrain.Spawn,
            new(collision.Spawn.Rotation[0], collision.Spawn.Rotation[1], collision.Spawn.Rotation[2]));
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
        // The source picks its screen settings from the collision the player stands on; the build starts with the arrival's.
        // lazy: one selection for the whole map; other collisions select other rows and need the host's per-area switching.
        var arrival = SourceAtmosphereReader.Selections(source)[terrain.Arrival];
        var focus = SourceAtmosphereReader.DepthOfField(source);
        if (!focus.TryGetValue(arrival.DepthOfField, out var arrivalFocus)) throw new InvalidDataException("Arrival selects a missing depth-of-field row");
        files.Add("mod/param/drawparam/mapbase.gparam.dcx", HostAtmosphere.FocusLikeSource(archive.Read("param/drawparam/mapbase.gparam.dcx"), arrivalFocus));
        // The display stage shows the scene as the source does, at the arrival row's exposure.
        // lazy: the exposure of an eye adapted to a bright scene; the source adapts between this and about twice it.
        var tones = SourceAtmosphereReader.ToneMap(source);
        if (!tones.TryGetValue(arrival.ToneMap, out var arrivalTone)) throw new InvalidDataException("Arrival selects a missing tone-map row");
        if (!SourceAtmosphereReader.ToneCorrection(source).TryGetValue(arrival.ToneCorrection, out var arrivalCorrection))
            throw new InvalidDataException("Arrival selects a missing tone-correction row");
        const string post = "shader/GXPostEffect.shaderbnd.dcx", toneStage = "ToneMap_TabledToneMapLUTPS.ppo";
        var postArchive = archive.Read(post);
        var toneProgram = ShaderArchive.Read(postArchive, toneStage);
        files.Add("mod/" + post, ShaderArchive.Replace(postArchive, [toneStage],
            compiler.Assemble(DisplayShader.DisplayLikeSource(compiler.Disassemble(toneProgram), arrivalTone.BrightExposure, arrivalCorrection), toneProgram)));
        string graphics = $"param/drawparam/{Map[..6]}_{Map[7..9]}00.gparam.dcx";
        files.Add("mod/" + graphics, HostAtmosphere.RemoveHostEffects(archive.Read(graphics)));

        // Each model is lit, fogged and scattered by the rows its placements select.
        var rowOf = models.ToDictionary(m => m.Name, m =>
        {
            var rows = placements.Where(p => p.GetProperty("model").GetString() == m.Name)
                .Select(p => (Light: p.GetProperty("lighting").GetInt32(), Fog: p.GetProperty("fog").GetInt32(), Scattering: p.GetProperty("scattering").GetInt32())).Distinct().ToArray();
            return rows.Length == 1 ? rows[0] : throw new InvalidDataException("Scenery model is placed under several lighting or air rows: " + m.Name);
        }, StringComparer.Ordinal);
        // Lighting, air and the diffuse multiplier are compiled into the programs, so each combination has its own shader family and material.
        var airRows = SourceAirReader.Read(source);
        var keys = models.SelectMany(m => m.Surfaces.Values.Select(d => (Rows: rowOf[m.Name], Diffuse: d))).Distinct().Order().ToArray();
        string Family((int Light, int Fog, int Scattering) rows, (float, float, float, bool, float, float) diffuse) =>
            $"{GroundFamily}_{rows.Light}_{rows.Fog}_{rows.Scattering}_{Array.IndexOf(keys.Where(k => k.Rows == rows).Select(k => k.Diffuse).ToArray(), diffuse)}";
        var families = new SortedDictionary<string, (PixelShader.GroundLighting Lighting, SourceAir.Values? Air, PixelShader.ViewFade? Fade)>(StringComparer.Ordinal);
        foreach (var (rows, diffuse) in keys)
        {
            var values = SourceLightingParameters.Values(lighting[rows.Light], renderer);
            float[] Rgb(string field) => ((float[])values[("$Globals", field)])[..3];
            // lazy: a placement selecting no fog or scattering row (255) is left clear; such rows are not seen on lit surfaces here.
            SourceAir.Values? air = airRows.Fog.TryGetValue(rows.Fog, out var fog) && airRows.Scattering.TryGetValue(rows.Scattering, out var scattering)
                ? SourceAir.Constants(scattering, fog) : rows.Fog == 255 && rows.Scattering == 255 ? null
                : throw new InvalidDataException("Scenery selects a missing fog or scattering row");
            // An unlit surface shows its own colour: no environment light and a uniform unit hemisphere.
            float[] tint = [diffuse.Item1, diffuse.Item2, diffuse.Item3];
            families.Add(Family(rows, diffuse), (diffuse.Item4 ? new([0, 0, 0], [1, 1, 1], [1, 1, 1], tint)
                : new(Rgb("gFC_EnvDifMapMulCol").Select(v => v * environmentSettings.Strength).ToArray(), Rgb("gFC_HemAmbCol_u"), Rgb("gFC_HemAmbCol_d"), tint), air,
                diffuse.Item6 == 0 ? null : new PixelShader.ViewFade(diffuse.Item5, diffuse.Item6)));
        }
        var environments = models.ToDictionary(m => m.Name, m =>
        {
            var textures = lighting[rowOf[m.Name].Light].DiffuseEnvironment.Textures;
            if (textures.Length != 1) throw new InvalidDataException("Lighting row lacks one diffuse environment texture");
            return $"EnvDif_m10_{textures[0]:D3}";
        }, StringComparer.Ordinal);
        var required = models.SelectMany(m => m.Textures).Concat(environments.Values)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var environmentBinder = BND4.Read(archive.Read($"map/m10/{Map}/{Map}_envmap_00_high_00.tpfbnd.dcx"));
        var textureTemplate = environmentBinder.Files.Select(f => DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes)
            .FirstOrDefault(b => b.Length > 36 && b[25] == 1 && new DDS(TextureReader.ReadPackage(b).First().Bytes).GetDXGIFormat() == DDS.DXGI_FORMAT.BC6H_UF16)
            ?? throw new InvalidDataException("Host install lacks an HDR cube texture template");
        // Texture packages are stored compressed like the host's other loose map files.
        DCX.Decompress(archive.Read($"map/m10/{Map}/{Map}_000000.mapbnd.dcx"), out var packageCompression);
        var textureWriter = new TextureWriter(textureTemplate, packageCompression);
        var raw = TextureReader.Read(source, required.Where(n => !NeutralTextures.Is(n))).ToDictionary(t => t.Name, t => t.Bytes, StringComparer.Ordinal);
        foreach (string neutral in required.Where(NeutralTextures.Is)) raw.Add(neutral, NeutralTextures.Bytes(neutral));
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

        var groundDefinitions = new SortedDictionary<string, byte[]>(StringComparer.Ordinal); byte[]? groundMetadata = null;
        var cutouts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            var row = rowOf[model.Name];
            using var document = JsonDocument.Parse(reader.ReadModel(model.Name));
            var selected = document.RootElement.GetProperty("surfaces").EnumerateArray()
                .Where(s => model.Surfaces.ContainsKey(s.GetProperty("index").GetInt32())).ToArray();
            var surfaces = selected.Select(s => LayerGeometry.Pack(LayerGeometry.Read(s))).ToArray();
            var bindings = selected.Select(s =>
            {
                string family = Family(row, model.Surfaces[s.GetProperty("index").GetInt32()]);
                var bound = MaterialWriter.BindLayers(hostDefinition.Bytes, metadata, donorDefinition, donorMetadata,
                    LayerMaterial.Read(s.GetProperty("material")), names, names[environments[model.Name]]);
                var definition = MATBIN.Read(bound.Definition);
                // The coverage test is compiled into the pixel stage, so a cutout has its own family beside the opaque one.
                if (LayerMaterial.Cutout(s.GetProperty("material")))
                {
                    family += "_cutout";
                    cutouts.Add(family);
                }
                definition.SourcePath = Rename(definition.SourcePath, family); definition.ShaderPath = Rename(definition.ShaderPath, family);
                bound = bound with { Definition = definition.Write() };
                groundMetadata ??= bound.Metadata;
                if (!groundDefinitions.TryAdd(family, bound.Definition) && !bound.Definition.AsSpan().SequenceEqual(groundDefinitions[family])
                    || !bound.Metadata.AsSpan().SequenceEqual(groundMetadata))
                    throw new InvalidDataException("Opaque surfaces need more than one material definition per shader family");
                return bound;
            }).ToArray();
            string digits = scenery.Models[model.Name];
            var binder = BND4.Read(archive.Read(modelTemplatePath));
            var file = binder.Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal));
            file.Bytes = LayerModelWriter.Write(modelTemplate, layerTemplate, exemplar, surfaces, bindings);
            file.Name = file.Name.Replace($"{Map}_000000", $"{Map}_{digits}");
            binder.Files = [file];
            Add($"map/m10/{Map}/{Map}_{digits}.mapbnd.dcx", binder.Write());
        }
        if (groundMetadata is null) throw new InvalidDataException("Source area has no ground surfaces");
        foreach (var (family, definition) in groundDefinitions)
        {
            string definitionName = Rename(hostDefinition.Name, family);
            if (definitions.Files.Any(f => f.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Ground material identity is occupied");
            definitions.Files.Add(new BinderFile(hostDefinition.Flags, definitions.Files.Max(f => f.ID) + 1, definitionName, definition));
        }
        files.Add("mod/material/allmaterial.matbinbnd.dcx", definitions.Write());
        definitions = null!;

        // The ground model carries a second tangent and both bitangents; its G-buffer vertex stages forward them and
        // its G-buffer pixel stage shades both layers with the row's source lighting.
        var slots = MaterialWriter.LayerSlots(groundMetadata);
        var emitting = ShaderMetadata.EnableEmission(groundMetadata);
        var vertexStages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        // The family's vertex variants differ in their outputs but feed one pixel stage: place the added outputs after all of them.
        int outputRow;
        using (var family = new BND4Reader(ShaderArchive.ReadFamily(shaders, HostFamily + ".shaderbdle")))
            outputRow = family.Files.Where(f => Regex.IsMatch(f.Name, @"_Gbuf[^\\/]*\.vpo$"))
                .Max(f => VertexShader.FreeOutputRow(compiler.Disassemble(family.ReadFile(f))));
        // With air, the vertex stage also forwards the vector to the camera and the view depth.
        string Forward(string module, bool view)
        {
            string frames = VertexShader.AddLayerFrames(module, outputRow);
            return view ? VertexShader.AddView(frames, outputRow + 3) : frames;
        }
        var plain = compiler.Disassemble(ShaderArchive.Read(shaders, HostFamily + ".shaderbdle", HostFamily + "_Gbuf.vpo"));
        files.Add("mod/shader/shaderbdle.shaderbdlebnd.dcx", ShaderArchive.AddFamilies(shaders, HostFamily,
            families.Concat(cutouts.Select(name => KeyValuePair.Create(name, families[name[..^"_cutout".Length]])))
                .OrderBy(family => family.Key, StringComparer.Ordinal).Select(family => (family.Key, (Func<string, byte[], byte[]>)((suffix, bytes) =>
            {
                var (light, air, fade) = family.Value;
                bool cut = cutouts.Contains(family.Key), view = air is not null || fade is not null && cut;
                if (suffix == ".metaparam") return emitting;
                if (Regex.IsMatch(suffix, @"^_Gbuf.*\.vpo$"))
                {
                    string stageKey = suffix + "|" + (view ? "view" : "plain");
                    if (!vertexStages.TryGetValue(stageKey, out var stage))
                        vertexStages.Add(stageKey, stage = compiler.Assemble(Forward(compiler.Disassemble(bytes), view), bytes));
                    return stage;
                }
                if (suffix is "_Gbuf.ppo" or "_Gbuf_[A].ppo")
                    // lazy: the source's half-coverage threshold, as in the accepted playtest; no material here overrides it.
                    return compiler.Assemble(PixelShader.ComposeLayeredGround(compiler.Disassemble(bytes), Forward(plain, view), slots, HostTargets, light,
                        cut ? 128 / 255f : null, air, cut ? fade : null), bytes);
                return bytes;
            }))).ToArray()));

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
            textures = names,
            groundFamilies = families.ToDictionary(f => f.Key, f => new { lighting = f.Value.Lighting, air = f.Value.Air, fade = f.Value.Fade }),
            cutoutFamilies = cutouts,
            arrival = new { collision = terrain.Arrival, selection = arrival, focus = arrivalFocus, tone = arrivalTone, exposure = arrivalTone.BrightExposure, correction = arrivalCorrection },
            unbuiltSurfaces = unbuilt,
            excludedCollision = terrain.Excluded.Select(p => new { p.Name, p.Role, p.Disabled }),
            files = files.Files.Select(f => new { path = f.Key, size = f.Value.Size, sha256 = f.Value.Hash }).ToArray(),
        };
        files.Add("manifest.json", System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n"));
        Console.WriteLine($"Built {files.Files.Count} files. {unbuilt.Values.Sum()} source surfaces in {unbuilt.Count} material families are not built yet.");
    }

    private static string Rename(string path, string family) => path.Contains(HostFamily, StringComparison.Ordinal)
        ? path.Replace(HostFamily, family) : throw new InvalidDataException("Host material path lacks its family name");
}
