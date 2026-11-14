using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold.Archives;
using AgeOfGold.Collision;
using AgeOfGold.Effects;
using AgeOfGold.Maps;
using AgeOfGold.Navigation;
using AgeOfGold.Params;
using AgeOfGold.Scene;
using AgeOfGold.Shaders;
using AgeOfGold.Talk;
using AgeOfGold.Text;
using SoulsFormats;

namespace AgeOfGold.Building;

/// <summary>Reads both installs and writes the Firelink build: a mod directory, its mod loader profile and a manifest.</summary>
internal static class FirelinkBuild
{
    private const string Map = "m10_01_00_00", HostFamily = "M[AMSN_V][Mb2]", DonorFamily = "M[AMSN][Mb2]", GroundFamily = "aog_ground";
    // The host's deferred targets as its installed opaque programs write them: encoded normal, surface colour, emitted light.
    // lazy: zero emission weight keeps the host from adding glow; the source local lights need the light-composition stage and its marker.
    private static readonly PixelShader.Targets HostTargets = new(0, 2, 4, 0);
    // The host's forward targets as its installed blended programs write them: colour with opacity, then opacity alone.
    private static readonly PixelShader.Blended HostForward = new(0, 1);
    // The host's visual-effect row for the leaves that drift around the player everywhere outdoors.
    private const int HostDriftingLeaves = 200000;
    // The source's effect for a lit, unkindled bonfire, and the host identities reserved for its translation and textures.
    // lazy: the lit state only; unlit and kindled bonfires use neighbouring source effects and need the resting point's state.
    // The Fire Keeper's interaction: the source talk identity that places it, the source texts of its prompt and first response,
    // and the host identities reserved for its talk script, action, prompt and message. The host action row copied is its plain "talk".
    // lazy: one fixed response in English from authored source text identities; the source's talk scripts, services, flags and
    // other languages are not translated.
    private const int SourceKeeperTalk = 102606, SourceKeeperPrompt = 10010200, SourceKeeperResponse = 10010191;
    private const int KeeperTalk = 100190000, KeeperAction = 9100000, KeeperPrompt = 9100000, KeeperMessage = 9100001, HostTalkAction = 6000;
    private const int SourceBonfireFire = 90001, BonfireFire = 990100300, BonfireFireTextures = 990300;

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
        var walkable = SourceNavigationReader.Read(source);
        var lighting = SourceLightingReader.Read(source).Scenery.ToDictionary(r => r.Id);
        using var placementDocument = JsonDocument.Parse(reader.ReadPlacements());
        // lazy: of the source's objects only the bonfire is built, as fixed scenery; the rest are counted in the manifest
        // until breakable, animated and interactive objects are translated.
        string[] sceneryObjects = ["o0200"];
        using var objectDocument = JsonDocument.Parse(reader.ReadObjectPlacements());
        var objectPlacements = objectDocument.RootElement.EnumerateArray().ToLookup(p => sceneryObjects.Contains(p.GetProperty("model").GetString()));
        var unbuiltObjects = new SortedDictionary<string, int>(objectPlacements[false].GroupBy(p => p.GetProperty("model").GetString()!)
            .ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        // lazy: of the source's characters only the Fire Keeper is built, as scenery held at the first frame of her idle animation;
        // living characters need host skeletons, animation and behaviour. The rest are counted in the manifest.
        var sceneryCharacters = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["c2750"] = "a00_0000" };
        using var characterDocument = JsonDocument.Parse(reader.ReadCharacterPlacements());
        var characterPlacements = characterDocument.RootElement.EnumerateArray().ToLookup(p => sceneryCharacters.ContainsKey(p.GetProperty("model").GetString()!));
        var unbuiltCharacters = new SortedDictionary<string, int>(characterPlacements[false].GroupBy(p => p.GetProperty("model").GetString()!)
            .ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        var placements = placementDocument.RootElement.EnumerateArray().Concat(objectPlacements[true]).Concat(characterPlacements[true]).ToArray();
        byte[] ReadModel(string name) => sceneryCharacters.TryGetValue(name, out var held) ? reader.ReadCharacter(name, held)
            : sceneryObjects.Contains(name) ? reader.ReadObject(name) : reader.ReadModel(name);

        // lazy: only the opaque lit families are built; every other surface is counted in the manifest until its family lands.
        // First pass: classify each model's surfaces and collect texture names; geometry is read again when its model is built.
        var renderer = SourceRendererSettings.Read(source);
        var environmentSettings = SourceEnvironmentSettings.Read(source, toolchain);
        var models = new List<(string Name, Dictionary<int, (float R, float G, float B, bool Unlit, float FadeOffset, float FadeScale, float ScrollU, float ScrollV)> Surfaces, string[] Textures)>();
        foreach (string name in reader.Models.Concat(sceneryObjects).Concat(sceneryCharacters.Keys))
        {
            using var model = JsonDocument.Parse(ReadModel(name));
            var ground = new Dictionary<int, (float, float, float, bool, float, float, float, float)>(); var textures = new List<string>();
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
                // The source slides a material's coordinates at a fixed rate; only the first rate applies to these one-texture-set families.
                float[] Rate(int index) => parameters.TryGetProperty("g_TexScroll_" + index, out var rate) ? rate.EnumerateArray().Select(v => v.GetSingle()).ToArray() : [0, 0];
                int scrollType = parameters.TryGetProperty("g_TexScrollType", out var declaredScroll) ? declaredScroll.GetInt32() : 0;
                if (scrollType is not (0 or 1) || Rate(0).Length != 2 || Rate(1).Concat(Rate(2)).Any(v => v != 0) || scrollType == 0 && Rate(0).Any(v => v != 0))
                    throw new InvalidDataException("Material scrolls its textures in an unclassified way: " + name);
                ground.Add(surface.GetProperty("index").GetInt32(), (tint[0], tint[1], tint[2], lightingType == 0, fade?.Offset ?? 0, fade?.Scale ?? 0, Rate(0)[0], Rate(0)[1]));
                var layers = LayerMaterial.Read(material);
                textures.AddRange(new[] { layers.First, layers.Second }.SelectMany(l => new[] { l.Colour, l.Normal, l.Packed }));
            }
            if (ground.Count != 0) models.Add((name, ground, textures.ToArray()));
        }

        var layout = MSBE.Read(archive.Read($"map/mapstudio/{Map}.msb.dcx"));
        var effectRegion = layout.Regions.SFX.FirstOrDefault() ?? throw new InvalidDataException("Host map lacks an effect region to place effects through");
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
        // The source bonfire becomes a host resting point: marker, controller and respawn region copied from the host's own
        // first resting point, with identities and an activation flag reserved in this map's range.
        var sourceLayout = MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb"));
        var bonfires = sourceLayout.Parts.Objects.Where(o => o.ModelName == "o0200").ToArray();
        if (bonfires.Length != 1) throw new InvalidDataException("Expected one source bonfire in the area");
        const string restingMap = "m18_00_00_00";
        var restingLayout = MSBE.Read(archive.Read($"map/mapstudio/{restingMap}.msb.dcx"));
        var registration = EMEVD.Read(archive.Read($"event/{restingMap}.emevd.dcx")).Events.Single(e => e.ID == 0).Instructions
            .FirstOrDefault(i => i.Bank == 2009 && i.ID == 3 && i.ArgData.Length == 24) ?? throw new InvalidDataException("Host resting-point registration not found");
        uint hostMarker = BitConverter.ToUInt32(registration.ArgData, 4);
        var marker = restingLayout.Parts.Assets.Single(p => p.EntityID == hostMarker);
        var controller = restingLayout.Parts.Enemies.Single(p => p.EntityID == hostMarker - 1000);
        var restingTemplate = new FirelinkBonfire.Template(marker, controller, restingLayout.Regions.SpawnPoints.Single(r => r.EntityID == hostMarker + 1000),
            restingLayout.Models.Assets.Single(m => m.Name == marker.ModelName), restingLayout.Models.Enemies.Single(m => m.Name == controller.ModelName), registration);
        var taken = layout.Parts.GetEntries().Select(p => p.EntityID).Concat(layout.Regions.GetEntries().Select(r => r.EntityID)).ToHashSet();
        // lazy: fifty reserved identities at the top of this map's object range; a fuller map needs an allocator.
        uint restingEntity = Enumerable.Range(10011950, 50).Select(id => (uint)id).First(id => !taken.Contains(id) && !taken.Contains(id - 1000) && !taken.Contains(id + 1000));
        int restingFlag = 10010000 + (int)(restingEntity - 10011950);
        var resting = new FirelinkBonfire.Placement("aog_bonfire", restingEntity, restingFlag, bonfires[0].Position + terrain.Offset, bonfires[0].Rotation);
        // The bonfire's fire burns at the object's attachment point, turned with the object.
        var sourceEffects = BND3.Read(source.Read("sfx/FRPG_SfxBnd_CommonEffects.ffxbnd.dcx"));
        byte[] SourceEffectFile(string leaf) => sourceEffects.Files.Single(f => f.Name.Replace('\\', '/').EndsWith("/" + leaf, StringComparison.OrdinalIgnoreCase)).Bytes;
        var fire = SourceEffectReader.Read(SourceEffectFile($"f{SourceBonfireFire:D7}.ffx"));
        if (fire.Clusters.Length == 0) throw new InvalidDataException("Source bonfire effect has no translatable flames");
        const string hostEffects = "sfx/sfxbnd_commoneffects.ffxbnd.dcx";
        var addedFire = EffectBundle.Add(archive.Read(hostEffects), BonfireFire, BonfireFireTextures, fire.Clusters,
            id => TextureReader.ReadPackage(SourceEffectFile($"s{id:D5}.tpf")).Single().Bytes);
        files.Add("mod/" + hostEffects, addedFire.Bundle);
        if (bonfires[0].Rotation.X != 0 || bonfires[0].Rotation.Z != 0) throw new InvalidDataException("Source bonfire is tilted");
        var fireAt = resting.Position + Vector3.Transform(reader.ReadObjectAttachment("o0200"), Matrix4x4.CreateRotationY(bonfires[0].Rotation.Y * MathF.PI / 180));
        FirelinkMapWriter.AddEffect(layout, effectRegion, "aog_bonfire_fire", BonfireFire, fireAt, bonfires[0].Rotation);
        var (restingMapBytes, restingEvents) = FirelinkBonfire.Apply(layout.Write(), archive.Read($"event/{Map}.emevd.dcx"), restingTemplate, resting);
        // The Fire Keeper is spoken to through an unseen controller where the source places her talk.
        var keepers = sourceLayout.Parts.Enemies.Where(p => p.TalkID == SourceKeeperTalk).ToArray();
        if (keepers.Length != 1) throw new InvalidDataException("Expected one source placement for the Fire Keeper's talk");
        var spoken = MSBE.Read(restingMapBytes);
        var occupied = spoken.Parts.GetEntries().Select(p => p.EntityID).Concat(spoken.Regions.GetEntries().Select(r => r.EntityID)).ToHashSet();
        // lazy: the same reserved range as the resting point's controller, from its far end.
        uint keeperEntity = Enumerable.Range(0, 40).Select(i => (uint)(10010990 - i)).First(id => !occupied.Contains(id));
        FirelinkMapWriter.AddTalker(spoken, spoken.Parts.Enemies.Single(p => p.EntityID == restingEntity - 1000), "aog_firekeeper_talk", keeperEntity, KeeperTalk,
            keepers[0].Position + terrain.Offset, keepers[0].Rotation);
        files.Add($"mod/map/mapstudio/{Map}.msb.dcx", spoken.Write());
        files.Add($"mod/event/{Map}.emevd.dcx", restingEvents);
        // Her talk script joins the map's own talk bundle, or an emptied copy of the resting template map's when the map has none.
        string talkPath = $"script/talk/{Map}.talkesdbnd.dcx", talkName = $"t{KeeperTalk:D9}.esd";
        bool ownTalk = archive.Contains(talkPath);
        var talkBundle = BND4.Read(archive.Read(ownTalk ? talkPath : $"script/talk/{restingMap}.talkesdbnd.dcx"));
        if (talkBundle.Files.Count == 0) throw new InvalidDataException("Host talk bundle template is empty");
        string talkFolder = talkBundle.Files[0].Name[..(talkBundle.Files[0].Name.LastIndexOf('\\') + 1)];
        var talkFlags = talkBundle.Files[0].Flags;
        if (!ownTalk) { talkBundle.Files.Clear(); talkFolder = talkFolder.Replace(restingMap, Map); }
        if (!talkFolder.Contains(Map, StringComparison.Ordinal) || talkBundle.Files.Any(f => f.Name.EndsWith(talkName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Host talk bundle cannot take the build's talk script");
        talkBundle.Files.Add(new(talkFlags, talkBundle.Files.Select(f => f.ID).DefaultIfEmpty(-1).Max() + 1, talkFolder + talkName, TalkScript.OneMessage(KeeperAction, KeeperMessage)));
        files.Add("mod/" + talkPath, talkBundle.Write());
        // Her prompt and response are the source's own words, added to the host's newest menu text bundle.
        const string hostText = "msg/engus/menu_dlc02.msgbnd.dcx";
        var sourceText = source.Read("msg/ENGLISH/menu.msgbnd.dcx");
        files.Add("mod/" + hostText, MessageBundle.Add(archive.Read(hostText), [
            ("ActionButtonText.fmg", KeeperPrompt, MessageBundle.ReadSource(sourceText, "Event_text_.fmg", SourceKeeperPrompt)),
            ("EventTextForTalk.fmg", KeeperMessage, MessageBundle.ReadSource(sourceText, "Event_text_.fmg", SourceKeeperResponse))]));
        // The source picks its screen settings from the collision the player stands on; the build starts with the arrival's.
        // lazy: one selection for the whole map; other collisions select other rows and need the host's per-area switching.
        var selections = SourceAtmosphereReader.Selections(source);
        var arrival = selections[terrain.Arrival];
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

        var characterSupports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        // Each model is lit, fogged and scattered by the rows its placements select.
        var rowOf = models.ToDictionary(m => m.Name, m =>
        {
            var rows = placements.Where(p => p.GetProperty("model").GetString() == m.Name).Select(p =>
            {
                // A character takes the rows of the collision it stands on, as the source lights whoever walks there.
                if (!sceneryCharacters.ContainsKey(m.Name))
                    return (Light: p.GetProperty("lighting").GetInt32(), Fog: p.GetProperty("fog").GetInt32(), Scattering: p.GetProperty("scattering").GetInt32());
                string support = terrain.Support(V(p.GetProperty("position")) + terrain.Offset);
                characterSupports[p.GetProperty("name").GetString()!] = support;
                return (selections[support].Light, selections[support].Fog, selections[support].Scattering);
            }).Distinct().ToArray();
            return rows.Length == 1 ? rows[0] : throw new InvalidDataException("Scenery model is placed under several lighting or air rows: " + m.Name);
        }, StringComparer.Ordinal);
        // Lighting, air and the diffuse multiplier are compiled into the programs, so each combination has its own shader family and material.
        var airRows = SourceAirReader.Read(source);
        // Translated particles carry a marker; the host's particle stage gives those the source's colour response and fogged opacity.
        // lazy: the frame-blending textured stage only, and the fog of the bonfire's own row; other particle stages and rows follow with their effects.
        if (!airRows.Fog.TryGetValue(bonfires[0].FogID, out var fireFog) || !airRows.Scattering.TryGetValue(bonfires[0].ScatterID, out var fireScattering))
            throw new InvalidDataException("Source bonfire selects a missing fog or scattering row");
        var fireAir = SourceAir.Constants(fireScattering, fireFog);
        const string effectShaders = "shader/GXFfxShader.shaderbnd.dcx", particleStage = "GXFfxtessellateBlendTexture.ppo";
        var effectArchive = archive.Read(effectShaders);
        var particleProgram = ShaderArchive.Read(effectArchive, particleStage);
        files.Add("mod/" + effectShaders, ShaderArchive.Replace(effectArchive, [particleStage], compiler.Assemble(EffectShader.RespondLikeSource(
            compiler.Disassemble(particleProgram), HostEffectWriter.ResponseMarker, new(fireAir.FogBegin, fireAir.FogScale, fireAir.FogLimit)), particleProgram)));
        var keys = models.SelectMany(m => m.Surfaces.Values.Select(d => (Rows: rowOf[m.Name], Diffuse: d))).Distinct().Order().ToArray();
        string Family((int Light, int Fog, int Scattering) rows, (float, float, float, bool, float, float, float, float) diffuse) =>
            $"{GroundFamily}_{rows.Light}_{rows.Fog}_{rows.Scattering}_{Array.IndexOf(keys.Where(k => k.Rows == rows).Select(k => k.Diffuse).ToArray(), diffuse)}";
        var families = new SortedDictionary<string, (PixelShader.GroundLighting Lighting, SourceAir.Values? Air, PixelShader.ViewFade? Fade, (float U, float V) Scroll)>(StringComparer.Ordinal);
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
                diffuse.Item6 == 0 ? null : new PixelShader.ViewFade(diffuse.Item5, diffuse.Item6), (diffuse.Item7, diffuse.Rest.Item1)));
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
        // An object's textures come from its own binder; every other name is looked up in the map's packages.
        var raw = sceneryObjects.SelectMany(reader.ReadObjectTextures).Concat(sceneryCharacters.Keys.SelectMany(reader.ReadCharacterTextures))
            .Where(t => required.Contains(t.Name)).DistinctBy(t => t.Name, StringComparer.Ordinal).ToDictionary(t => t.Name, t => t.Bytes, StringComparer.Ordinal);
        foreach (var texture in TextureReader.Read(source, required.Where(n => !NeutralTextures.Is(n) && !raw.ContainsKey(n)).ToArray())) raw.Add(texture.Name, texture.Bytes);
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
        var cutouts = new SortedSet<string>(StringComparer.Ordinal); var blends = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            var row = rowOf[model.Name];
            using var document = JsonDocument.Parse(ReadModel(model.Name));
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
                // A blended surface is drawn by the family's forward stages, over the scene and without writing depth.
                else if (LayerMaterial.Blended(s.GetProperty("material")))
                {
                    // The host numbers its blend modes as the source does: two lays the surface over the scene, four adds it.
                    bool additive = LayerMaterial.Additive(s.GetProperty("material"));
                    family += additive ? "_add" : "_blend";
                    blends.Add(family);
                    definition.Params.Single(p => p.Name == "g_BlendMode").Value = additive ? 4 : 2;
                    foreach (string flag in new[] { "g_GX_bDisableDepthWrite", "g_GX_bDisableShadowCasting" }) definition.Params.Single(p => p.Name == flag).Value = true;
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
        byte[] HostStage(string suffix) => ShaderArchive.Read(shaders, HostFamily + ".shaderbdle", HostFamily + suffix);
        var forwardStages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string BaseFamily(string name) => cutouts.Contains(name) ? name[..^"_cutout".Length] : blends.Contains(name) ? name[..name.LastIndexOf('_')] : name;
        var plain = compiler.Disassemble(HostStage("_Gbuf.vpo"));
        // Scrolling surfaces follow the frame clock the host's forward programs read.
        var clock = PixelShader.FindClock(compiler.Disassemble(HostStage("_Fwd.ppo")));
        files.Add("mod/shader/shaderbdle.shaderbdlebnd.dcx", ShaderArchive.AddFamilies(shaders, HostFamily,
            // Only families a built surface uses are written; a material may exist only as a cutout.
            groundDefinitions.Keys.Select(name => KeyValuePair.Create(name, families[BaseFamily(name)]))
                .Select(family => (family.Key, (Func<string, byte[], byte[]>)((suffix, bytes) =>
            {
                var (light, air, fade, rate) = family.Value;
                var scroll = rate == (0, 0) ? null : new PixelShader.Scroll(rate.U, rate.V, clock);
                bool cut = cutouts.Contains(family.Key), blend = blends.Contains(family.Key), view = air is not null || fade is not null && (cut || blend);
                if (suffix == ".metaparam") return emitting;
                if (blend)
                {
                    // The forward stages take the deferred pair's place: the same forwarded frames and lighting, written with opacity.
                    var vertexStage = Regex.Match(suffix, @"^_Fwd(@\[cl\])?\.vpo$");
                    if (vertexStage.Success)
                    {
                        string deferred = "_Gbuf" + vertexStage.Groups[1].Value + ".vpo", stageKey = deferred + "|" + (view ? "view" : "plain");
                        if (!vertexStages.TryGetValue(stageKey, out var stage))
                        {
                            var host = HostStage(deferred);
                            vertexStages.Add(stageKey, stage = compiler.Assemble(Forward(compiler.Disassemble(host), view), host));
                        }
                        return stage;
                    }
                    var pixelStage = Regex.Match(suffix, @"^_Fwd(?:Sdw|SSdw)?(_\[A\])?\.ppo$");
                    if (!pixelStage.Success) return bytes;
                    string deferredPixel = "_Gbuf" + pixelStage.Groups[1].Value + ".ppo", pixelKey = family.Key + "|" + deferredPixel;
                    if (!forwardStages.TryGetValue(pixelKey, out var composed))
                    {
                        var host = HostStage(deferredPixel);
                        forwardStages.Add(pixelKey, composed = compiler.Assemble(PixelShader.ComposeLayeredGround(compiler.Disassemble(host), Forward(plain, view), slots, HostTargets, light,
                            null, air, fade, HostForward, scroll), host));
                    }
                    return composed;
                }
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
                        cut ? 128 / 255f : null, air, cut ? fade : null, null, scroll), bytes);
                return bytes;
            }))).ToArray()));

        // Navigation: the source's walkable triangles become the map's one host navigation mesh, written into the first
        // installed mesh file that has faces, and the map's index lists that mesh alone.
        string meshBundlePath = $"map/m10/{Map}/{Map}.nvmhktbnd.dcx", meshIndexPath = $"map/m10/{Map}/{Map}.nva.dcx";
        var meshBundle = BND4.Read(archive.Read(meshBundlePath));
        var meshFile = meshBundle.Files.OrderBy(f => f.ID).FirstOrDefault(f => HostNavigationMesh.IsTemplate(f.Bytes))
            ?? throw new InvalidDataException("Host map has no navigation mesh to build on");
        var meshName = Regex.Match(meshFile.Name, @"_(\d{6})\.hkx$");
        if (!meshName.Success) throw new InvalidDataException("Unclassified host navigation mesh name");
        int meshModel = int.Parse(meshName.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        meshFile.Bytes = HostNavigationMesh.Write(meshFile.Bytes, walkable.Vertices.Select(v => v + terrain.Offset).ToArray(), walkable.Triangles);
        meshBundle.Files = [meshFile];
        files.Add("mod/" + meshBundlePath, meshBundle.Write());
        var meshIndex = DCX.Decompress(archive.Read(meshIndexPath), out var meshIndexCompression);
        files.Add("mod/" + meshIndexPath, DCX.Compress(HostNavigationIndex.OneMesh(meshIndex, meshModel, walkable.Triangles.Length), meshIndexCompression));

        // Host table edits are written back with the regulation's own encryption.
        var regulation = Regulation.Read(host.Read("eldenring.exe"), host.Read("regulation.bin"));
        var effectTable = regulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "SpEffectVfxParam.param");
        effectTable.Bytes = GameplayTables.SilenceEffect(effectTable.Bytes, HostDriftingLeaves);
        // The resting menu lists only the translated resting point. Its row follows a host row that names one destination.
        var destinations = regulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "BonfireWarpParam.param");
        var destinationTemplate = RestingTemplate(destinations.Bytes);
        // lazy: the host template's place name until source text is translated.
        destinations.Bytes = GameplayTables.ReplaceDestinations(destinations.Bytes, destinationTemplate.Entity,
            new(restingFlag, "aog_bonfire", restingEntity, (uint)restingFlag, 10, 1, 0, resting.Position, destinationTemplate.Text));
        var actions = regulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "ActionButtonParam.param");
        actions.Bytes = GameplayTables.AddInteraction(actions.Bytes, HostTalkAction, KeeperAction, "aog_firekeeper_talk", KeeperPrompt);
        files.Add("mod/regulation.bin", regulation.Write());

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
            groundFamilies = groundDefinitions.Keys.ToDictionary(name => name, name =>
            {
                var family = families[BaseFamily(name)];
                return new { lighting = family.Lighting, air = family.Air, fade = family.Fade, scroll = new[] { family.Scroll.U, family.Scroll.V }, cutout = cutouts.Contains(name), blended = blends.Contains(name) };
            }),
            restingPoint = new { entity = restingEntity, flag = restingFlag, position = new[] { resting.Position.X, resting.Position.Y, resting.Position.Z } },
            arrival = new { collision = terrain.Arrival, selection = arrival, focus = arrivalFocus, tone = arrivalTone, exposure = arrivalTone.BrightExposure, correction = arrivalCorrection },
            unbuiltSurfaces = unbuilt,
            unbuiltObjects,
            unbuiltCharacters,
            characters = characterSupports.ToDictionary(c => c.Key, c => new { collision = c.Value, rows = selections[c.Value] }),
            navigation = new { model = meshModel, vertices = walkable.Vertices.Length, faces = walkable.Triangles.Length, dropped = walkable.Dropped },
            talk = new { fireKeeper = new { entity = keeperEntity, script = KeeperTalk, action = KeeperAction, prompt = KeeperPrompt, message = KeeperMessage } },
            effects = new
            {
                bonfire = new { effect = BonfireFire, source = SourceBonfireFire, position = new[] { fireAt.X, fireAt.Y, fireAt.Z }, clusters = fire.Clusters.Length,
                    textures = addedFire.Textures, template = addedFire.Template, unbuilt = fire.Unbuilt },
            },
            excludedCollision = terrain.Excluded.Select(p => new { p.Name, p.Role, p.Disabled }),
            files = files.Files.Select(f => new { path = f.Key, size = f.Value.Size, sha256 = f.Value.Hash }).ToArray(),
        };
        files.Add("manifest.json", System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n"));
        Console.WriteLine($"Built {files.Files.Count} files. {unbuilt.Values.Sum()} source surfaces in {unbuilt.Count} material families are not built yet.");
    }

    // The first host destination whose entity appears once: a row to copy the unedited fields from.
    private static (uint Entity, int Text) RestingTemplate(byte[] table)
    {
        var rows = PARAM.Read(table);
        var definition = new PARAMDEF { ParamType = rows.ParamType, DataVersion = rows.ParamdefDataVersion };
        definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = (int)rows.DetectedSize });
        rows.ApplyParamdef(definition);
        uint Entity(PARAM.Row row) => BitConverter.ToUInt32((byte[])row["raw"].Value, 8);
        var template = rows.Rows.First(r => Entity(r) > 0 && rows.Rows.Count(other => Entity(other) == Entity(r)) == 1);
        return (Entity(template), BitConverter.ToInt32((byte[])template["raw"].Value, 48));
    }

    private static string Rename(string path, string family) => path.Contains(HostFamily, StringComparison.Ordinal)
        ? path.Replace(HostFamily, family) : throw new InvalidDataException("Host material path lacks its family name");
}
