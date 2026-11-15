using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold.Archives;
using AgeOfGold.Characters;
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
    // An emission weight below nothing marks the light as the build's own: the composition stage shows it alone, without
    // the host's diffuse light, reflections or glow.
    private static readonly PixelShader.Targets HostTargets = new(0, 2, 4, -1);
    // Measured in the host against the source game on the same bodies: how strongly an environment box must reflect its
    // cube for a source specular gain of one. At this strength leather and skin match; metal is dimmer than the source's.
    // lazy: one strength for every surface. The host reflects from anything not metal a fixed share several times the
    // source's, so matching both needs the host program's reflectance changed, not this number.
    private const float ReflectionScale = 0.3f / 33.63f;
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
    /// <summary>
    /// A source enemy built whole — its own body, skeleton, animations, physics and attack timing — with what the source
    /// has no counterpart for lent by host characters.
    /// </summary>
    /// <param name="Host">The host character that lends its behaviour, action script and mesh exemplars.</param>
    /// <param name="TemplateMap">A host map with a placement, <paramref name="TemplatePart"/>, of the host character, whose row and AI the enemy takes.</param>
    /// <param name="Body">A host material, by name fragment, whose mesh layout and inputs the source's body takes; <paramref name="Held"/> likewise for what it holds.</param>
    /// <param name="Shown">The source's optional meshes, by display mark, that are drawn.</param>
    /// <param name="Physics">A host character whose physics file uses every kind of object the source's needs.</param>
    /// <param name="Attack">A host attack row of a like attack; the source's hit sizes, attachment points and damage are laid over it.</param>
    /// <param name="Gaze">The source bones the host turns to look at something: the neck's first and last, the spine's first and last.</param>
    private sealed record Living(string Host, string TemplateMap, string TemplatePart, string Body, string Held, string[] Shown, string Physics, int Attack,
        (string NeckFrom, string NeckTo, string SpineFrom, string SpineTo)? Gaze);

    // The source's human-sized fighters take the behaviour and AI of the host's wandering noble, a slow, weak sword fighter
    // of about their build; the host's `c3020` lends its physics file's kinds of object, and the noble's sword swing its attack row.
    // lazy: the noble's AI, health and other row values; see WholeCharacter for what each of a character's files leaves out.
    // Every placement of a model shows the same weapons; the source's spear, axe and bow variants are chosen by its rows'
    // display marks, which are not read yet. The giant skeleton, the crow and the others are not built.
    private static Living LikeNoble(params string[] shown) => new("c4300", "m60_40_40_00", "c4300_9002", CharacterMaterial, CharacterMaterial, shown, "c3020", 4300100, ("Neck", "Head", "Spine", "Spine1"));
    private static readonly SortedDictionary<string, Living> LivingCharacters = new(StringComparer.Ordinal)
    {
        ["c2510"] = LikeNoble("#00#"), ["c2540"] = LikeNoble("#00#", "#02#"), ["c2550"] = LikeNoble("#02#", "#01#"), ["c2900"] = LikeNoble("#00#", "#01#"),
    };
    // Build-owned host characters take identities from here on; the host has none between c6321 and c6999.
    private const int FirstCharacterIdentity = 6400;
    /// <summary>
    /// A source human built whole: the source human's skeleton and physics under the part models of what the source
    /// dresses it in, standing in the pose it starts in, with what the source has no counterpart for lent by host characters.
    /// </summary>
    /// <param name="Host">The host character that lends its behaviour, action script and mesh exemplars.</param>
    /// <param name="FriendMap">A host map with a placement, <paramref name="FriendPart"/>, of a character that leaves the player alone; the person takes its standing.</param>
    private sealed record LivingPerson(string Host, string TemplateMap, string TemplatePart, string Body, string Held, string FriendMap, string FriendPart, string Physics,
        (string NeckFrom, string NeckTo, string SpineFrom, string SpineTo)? Gaze);

    // The humans who are at Firelink from the start, by source placement, on the behaviour of the host's wandering noble,
    // with the standing of a host character who leaves the player alone. Which humans are present at the start is authored
    // from knowledge of the source game: its event script decides by flags this tool does not yet follow.
    // lazy: no face front (the source generates it as the game runs) and no hair; one line of talk; only the pose each
    // starts in (see WholeCharacter.WritePerson); the host template's body size.
    private static LivingPerson Friendly() => new("c4300", "m18_00_00_00", "c4300_9009", CharacterMaterial, CharacterMaterial, "m11_10_00_00", "c0000_9010", "c3020", ("Neck", "Head", "Spine", "Spine1"));
    // The cleric by the ruined church and the warrior by the bonfire. The others the source places here arrive with
    // events, which the build does not run yet.
    private static readonly SortedDictionary<string, LivingPerson> LivingPeople = new(StringComparer.Ordinal) { ["c0000_0016"] = Friendly(), ["c0000_0018"] = Friendly() };
    // What a human holds hangs from the weapon bone of its hand.
    private const string RightHandBone = "R_Weapon", LeftHandBone = "L_Weapon";
    // A person's host identity follows the enemies' block: by its character row's place among the source's human rows.
    private const int FirstPersonIdentity = 6600;

    // Characters are drawn by the host's plain character material: a colour map, a relief map and a metal map, and nothing
    // of any host character's own blended in. The inputs a body fills: its colour, its relief with gloss, and its metal.
    // The source keeps metal and roughness in its specular map; the host's metal map and the gloss in its relief map are
    // made from it (HostCharacterTextures).
    // lazy: the source's specular level for what is not metal (the specular map's blue) is not carried; the host
    // reflects a fixed share there.
    private const string CharacterMaterial = "\\C[AMSN].matxml";
    private const string ColourInput = "_AlbedoMap_0", ReliefInput = "_NormalMap_4", MetalInput = "_MetallicMap_0";
    // The source cuts torn cloth, hair and mail out of flat sheets with materials it names as edged. Those meshes take a
    // definition of the build's own: the plain one with a coverage threshold.
    // lazy: half coverage; the source's own threshold is not read.
    private const string SourceCutout = "_Edge", CharacterDefinition = "aog_character";
    private const int CutoutReference = 128;
    // Build-owned character rows follow the host character's own, from this offset within its thousand.
    private const int FirstCharacterRow = 8000;

    // How far the walkable border moves out toward the walls: the source's own margin from them is about this.
    private const float WalkableReach = 0.5f;
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
        var sourceLighting = SourceLightingReader.Read(source);
        var lighting = sourceLighting.Scenery.ToDictionary(r => r.Id);
        using var placementDocument = JsonDocument.Parse(reader.ReadPlacements());
        // lazy: the source's objects that no event or item touches and that are not animated stand as fixed scenery: the
        // bonfire, the broken statue and the large urns. The urns cannot be broken, and none of these blocks the player. The
        // rest (chests, bodies holding items, the lift, the pool's covers, a lever) are counted in the manifest until
        // breakable, animated and interactive objects are translated. The list is authored from a reading of the map.
        string[] sceneryObjects = ["o0200", "o1470", "o1495", "o1496", "o1497"];
        using var objectDocument = JsonDocument.Parse(reader.ReadObjectPlacements());
        var objectPlacements = objectDocument.RootElement.EnumerateArray().ToLookup(p => sceneryObjects.Contains(p.GetProperty("model").GetString()));
        var unbuiltObjects = new SortedDictionary<string, int>(objectPlacements[false].GroupBy(p => p.GetProperty("model").GetString()!)
            .ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        // lazy: of the source's characters only the Fire Keeper is built, as scenery held at the first frame of her idle animation;
        // living characters need host skeletons, animation and behaviour. The rest are counted in the manifest.
        var sceneryCharacters = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["c2750"] = "a00_0000" };
        using var characterDocument = JsonDocument.Parse(reader.ReadCharacterPlacements());
        var characterPlacements = characterDocument.RootElement.EnumerateArray().ToLookup(p => sceneryCharacters.ContainsKey(p.GetProperty("model").GetString()!));
        var livingCharacters = LivingCharacters;
        var unbuiltCharacters = new SortedDictionary<string, int>(characterPlacements[false].Where(p => !livingCharacters.ContainsKey(p.GetProperty("model").GetString()!) && !LivingPeople.ContainsKey(p.GetProperty("name").GetString()!))
            .GroupBy(p => p.GetProperty("model").GetString()!).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        // A living character's meshes: its body, and the optional meshes shown. Each names the source textures it wears.
        var livingModels = livingCharacters.ToDictionary(c => c.Key, c =>
        {
            var file = BND3.Read(source.Read($"chr/{c.Key}.chrbnd.dcx")).Files.SingleOrDefault(f => f.Name.EndsWith(c.Key + ".flver", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Source character has no model: " + c.Key);
            var (model, _) = ModelReader.Read(file.Bytes);
            var meshes = Enumerable.Range(0, model.Meshes.Count).Select(i =>
            {
                var material = model.Materials[model.Meshes[i].MaterialIndex];
                string Texture(string input) => material.Textures.SingleOrDefault(t => t.ParamName == input) is { Path.Length: > 0 } t ? Path.GetFileNameWithoutExtension(t.Path.Replace('\\', '/'))
                    : throw new InvalidDataException($"Source character material lacks {input}: {c.Key}/{material.Name}");
                // A mesh marked "#NN#" is drawn only for rows that show that mark.
                string mark = material.Name.Length >= 4 && material.Name[0] == '#' && material.Name[3] == '#' ? material.Name[..4] : "";
                return (Mesh: i, Mark: mark, Colour: Texture("g_Diffuse"), Relief: Texture("g_Bumpmap"), Specular: Texture("g_Specular"), Cutout: material.MTD.Contains(SourceCutout, StringComparison.Ordinal), Tint: reader.SurfaceColours(material.MTD));
            }).Where(m => m.Mark == "" || c.Value.Shown.Contains(m.Mark)).ToArray();
            if (c.Value.Shown.Except(meshes.Select(m => m.Mark)).FirstOrDefault() is { } absent) throw new InvalidDataException($"Source character {c.Key} has no mesh marked {absent}");
            return (Model: model, Meshes: meshes);
        }, StringComparer.Ordinal);
        // Lighting and air are compiled into a model's programs, so an object placed under several rows is built once for
        // each: its commonest rows keep its name, and each other set of rows takes the name with a numbered suffix.
        const char Variant = '+';
        var sceneryObjectPlacements = objectPlacements[true].GroupBy(p => p.GetProperty("model").GetString()!, StringComparer.Ordinal).SelectMany(group =>
        {
            static (int, int, int) Rows(JsonElement p) => (p.GetProperty("lighting").GetInt32(), p.GetProperty("fog").GetInt32(), p.GetProperty("scattering").GetInt32());
            var order = group.GroupBy(Rows).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList();
            return group.Select(p =>
            {
                int variant = order.IndexOf(Rows(p));
                if (variant == 0) return p;
                var node = System.Text.Json.Nodes.JsonNode.Parse(p.GetRawText())!;
                node["model"] = $"{group.Key}{Variant}{variant}";
                using var renamed = JsonDocument.Parse(node.ToJsonString());
                return renamed.RootElement.Clone();
            });
        }).ToArray();
        var sceneryObjectModels = sceneryObjectPlacements.Select(p => p.GetProperty("model").GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var placements = placementDocument.RootElement.EnumerateArray().Concat(sceneryObjectPlacements).Concat(characterPlacements[true]).ToArray();
        byte[] ReadModel(string name) => sceneryCharacters.TryGetValue(name, out var held) ? reader.ReadCharacter(name, held)
            : sceneryObjectModels.Contains(name) ? reader.ReadObject(name.Split(Variant)[0]) : reader.ReadModel(name);

        // lazy: only the opaque lit families are built; every other surface is counted in the manifest until its family lands.
        // First pass: classify each model's surfaces and collect texture names; geometry is read again when its model is built.
        var renderer = SourceRendererSettings.Read(source);
        var environmentSettings = SourceEnvironmentSettings.Read(source, toolchain);
        var models = new List<(string Name, Dictionary<int, (float R, float G, float B, bool Unlit, float FadeOffset, float FadeScale, float ScrollU, float ScrollV)> Surfaces, string[] Textures)>();
        foreach (string name in reader.Models.Concat(sceneryObjectModels).Concat(sceneryCharacters.Keys))
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
        // The source picks its screen settings and a character's light from the collision stood on; the build starts with the arrival's.
        // lazy: one selection for the whole map; other collisions select other rows and need the host's per-area switching.
        var selections = SourceAtmosphereReader.Selections(source);
        var arrival = selections[terrain.Arrival];
        // The source lights a character by the row of the collision it stands on: an environment cube and a hemisphere
        // colour. The host lights its characters from light baked beside the map's environment points, which a build fills
        // with the source's.
        // lazy: the arrival's row for the whole map; the Fire Keeper's cell and other places select other rows, which need
        // volumes of their own for the environment boxes that cover them.
        var characterRow = sourceLighting.Characters.SingleOrDefault(r => r.Id == arrival.Light) ?? throw new InvalidDataException("Arrival selects a missing character lighting row");
        var characterValues = SourceLightingParameters.Values(characterRow, renderer);
        float[] CharacterColour(string field) => ((float[])characterValues[("$Globals", field)])[..3];
        if (characterRow.DiffuseEnvironment.Textures.Length != 1) throw new InvalidDataException("Character lighting row lacks one diffuse environment texture");
        string characterCubeName = $"EnvDif_m10_{characterRow.DiffuseEnvironment.Textures[0]:D3}";
        // The source reflects one sky cube at the row's specular gain; the host reflects each box's cube at the box's own strength.
        // lazy: a grey gain only; a tinted one needs the sky's colours scaled, which means writing HDR blocks.
        var reflectionGain = CharacterColour("gFC_EnvSpcMapMulCol").Select(v => v * environmentSettings.Strength).ToArray();
        if (reflectionGain.Max() - reflectionGain.Min() > 1e-3f * reflectionGain.Max()) throw new InvalidDataException("Character lighting row tints its reflections");
        if (characterRow.SpecularEnvironment.Textures.Length == 0) throw new InvalidDataException("Character lighting row lacks a specular environment texture");
        string characterSkyName = $"EnvSpc_m10_{characterRow.SpecularEnvironment.Textures[0]:D3}";
        FirelinkMapWriter.ReflectSky(layout, reflectionGain[0] * ReflectionScale);
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
        // Each living person's pieces: the part models of what the source dresses it in, with the textures those hold.
        var peoplePieces = LivingPeople.ToDictionary(person => person.Key, person =>
        {
            var placed = sourceLayout.Parts.Enemies.SingleOrDefault(p => p.Name == person.Key && p.ModelName == "c0000") ?? throw new InvalidDataException("Source map has no human placement " + person.Key);
            var dressed = SourceHumanReader.Read(source, placed.CharaInitID);
            var stems = dressed.Parts.Select(stem => (Stem: stem, Hand: "")).ToList();
            if (dressed.Right is { } right) stems.Add((right, RightHandBone));
            if (dressed.Left is { } left) stems.Add((left, LeftHandBone));
            return stems.Select(piece =>
            {
                var binder = BND3.Read(source.Read($"parts/{piece.Stem}.partsbnd.dcx"));
                // What is held comes with a second model for when it is put away; the first is the one in hand.
                var file = binder.Files.OrderBy(f => f.ID).FirstOrDefault(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Source part has no model: " + piece.Stem);
                // A part model may keep its vertices in several buffers (the face front does, for shaping); the format library joins them.
                var model = FLVER2.Read(file.Bytes);
                var meshes = Enumerable.Range(0, model.Meshes.Count).Select(i =>
                {
                    var material = model.Materials[model.Meshes[i].MaterialIndex];
                    string Texture(string input) => material.Textures.SingleOrDefault(t => t.ParamName == input) is { Path.Length: > 0 } t ? Path.GetFileNameWithoutExtension(t.Path.Replace('\\', '/'))
                        : throw new InvalidDataException($"Source part material lacks {input}: {piece.Stem}/{material.Name}");
                    // A material without a relief map (an eye, say) takes a flat one.
                    bool relief = material.Textures.Any(t => t.ParamName == "g_Bumpmap" && t.Path.Length > 0);
                    // A material without a specular map has neither metal nor gloss.
                    bool specular = material.Textures.Any(t => t.ParamName == "g_Specular" && t.Path.Length > 0);
                    return (Mesh: i, Colour: Texture("g_Diffuse"), Relief: relief ? Texture("g_Bumpmap") : NeutralTextures.FlatNormal, Specular: specular ? Texture("g_Specular") : "",
                        Cutout: material.MTD.Contains(SourceCutout, StringComparison.Ordinal), Tint: reader.SurfaceColours(material.MTD));
                }).ToArray();
                var held = binder.Files.Where(f => f.Name.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).SelectMany(f => TextureReader.ReadPackage(f.Bytes)).ToArray();
                return (piece.Stem, piece.Hand, Model: model, Meshes: meshes, Textures: held);
            }).ToArray();
        }, StringComparer.Ordinal);
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
        // Living enemies stand where the source places them, on the built collision the source names, each under a build-owned
        // row that is the host template's with the source's body size. A source space is sized for the source's bodies.
        var bodySizes = SourceCharacterReader.BodySizes(source);
        var characterRows = new List<(int Row, int Template, string Name, (float Height, float Radius)? Body, int? Mark, int? Standing, int? Variation)>();
        var enemies = new List<object>();
        // A source enemy takes a host identity by its place among the source's character models, so each source character
        // has one identity however many are built.
        var sourceModels = Directory.GetFiles(Path.GetDirectoryName(source.Locate("chr/c0000.chrbnd.dcx"))!, "c????.chrbnd.dcx").Select(f => Path.GetFileName(f)[..5])
            .Where(n => Regex.IsMatch(n, @"^c\d{4}$")).Order(StringComparer.Ordinal).ToArray();
        string Identity(string name)
        {
            int place = Array.IndexOf(sourceModels, name);
            if (place < 0 || FirstCharacterIdentity + place > 6999) throw new InvalidDataException("No host identity for the source character " + name);
            return $"c{FirstCharacterIdentity + place:D4}";
        }
        // What answers the enemies' attack events: the source's behaviour rows, and the attack rows they name by the host row each is laid over.
        var behaviourRows = new SortedDictionary<int, byte[]>(); var attackRows = new SortedDictionary<int, (int Template, byte[] Row)>();
        var attacksOf = new Dictionary<int, SourceCharacterReader.SourceAttacks>();
        foreach (var (name, living) in livingCharacters)
        {
            string identity = Identity(name);
            var donor = MSBE.Read(archive.Read($"map/mapstudio/{living.TemplateMap}.msb.dcx"));
            var template = donor.Parts.Enemies.SingleOrDefault(p => p.Name == living.TemplatePart && p.ModelName == living.Host)
                ?? throw new InvalidDataException($"Host map {living.TemplateMap} has no placement {living.TemplatePart} of {living.Host}");
            // A map lists each character model it places; an entry names its character by name and again in a path.
            // A whole character's entry is its host's under its own identity.
            if (!spoken.Models.Enemies.Any(m => m.Name == identity))
            {
                var listed = (MSBE.Model.Enemy)donor.Models.Enemies.Single(m => m.Name == living.Host).DeepCopy();
                if (listed.SibPath?.Contains(living.Host, StringComparison.Ordinal) != true) throw new InvalidDataException("Host model entry does not name its character in its path: " + living.Host);
                listed.Name = identity; listed.SibPath = listed.SibPath.Replace(living.Host, identity, StringComparison.Ordinal);
                spoken.Models.Enemies.Add(listed);
            }
            foreach (var placed in sourceLayout.Parts.Enemies.Where(p => p.ModelName == name).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (!bodySizes.TryGetValue(placed.NPCParamID, out var size)) throw new InvalidDataException($"Source placement {placed.Name} names a character row that is missing or has no body size");
                if (!attacksOf.TryGetValue(placed.NPCParamID, out var attacks))
                {
                    attacksOf[placed.NPCParamID] = attacks = SourceCharacterReader.Attacks(source, placed.NPCParamID);
                    foreach (var (id, row) in attacks.Behaviours) behaviourRows[id] = row;
                    foreach (var (id, row) in attacks.Hits) attackRows[id] = (living.Attack, row);
                }
                int known = characterRows.FindIndex(r => r.Template == template.NPCParamID && r.Body == size && r.Mark is null && r.Variation == attacks.Variation);
                if (known < 0)
                {
                    known = characterRows.Count;
                    characterRows.Add((template.NPCParamID / 10000 * 10000 + FirstCharacterRow + known, template.NPCParamID, $"aog_{name}_{known}", size, null, null, attacks.Variation));
                }
                int stoodOn = Array.IndexOf(terrain.Sources, placed.CollisionName);
                if (stoodOn < 0) throw new InvalidDataException($"Source placement {placed.Name} stands on a collision that is not built: {placed.CollisionName}");
                string floor = $"h{terrain.Low[stoodOn].Model}_0000";
                FirelinkMapWriter.AddEnemy(spoken, template, "aog_" + placed.Name, characterRows[known].Row, template.ThinkParamID, floor, placed.Position + terrain.Offset, placed.Rotation, 0, identity);
                enemies.Add(new { name = "aog_" + placed.Name, source = placed.Name, body = identity, row = characterRows[known].Row, behaviour = template.ThinkParamID, collision = floor,
                    height = size.Height, radius = size.Radius });
            }
        }
        var people = new List<object>();
        var humanRows = LivingPeople.Count == 0 ? [] : SourceCharacterReader.HumanRows(source);
        string PersonIdentity(int row)
        {
            int place = Array.IndexOf(humanRows, row);
            if (place < 0 || FirstPersonIdentity + place > 6999) throw new InvalidDataException("No host identity for the source human row " + row);
            return $"c{FirstPersonIdentity + place:D4}";
        }
        // A person runs a one-message talk script of its own that shows the source's opening line. (An unseen controller
        // standing in the same place, as the Fire Keeper has, is not offered to the player while the person stands there.)
        var peopleTalk = new List<(string Name, int Script, int Action, int Message, string Line)>();
        foreach (var (name, person) in LivingPeople)
        {
            var donor = MSBE.Read(archive.Read($"map/mapstudio/{person.TemplateMap}.msb.dcx"));
            var template = donor.Parts.Enemies.SingleOrDefault(p => p.Name == person.TemplatePart && p.ModelName == person.Host)
                ?? throw new InvalidDataException($"Host map {person.TemplateMap} has no placement {person.TemplatePart} of {person.Host}");
            var placed = sourceLayout.Parts.Enemies.Single(p => p.Name == name);
            string identity = PersonIdentity(placed.NPCParamID);
            if (spoken.Models.Enemies.Any(m => m.Name == identity)) throw new InvalidDataException("Two people share the character row of " + name);
            var listed = (MSBE.Model.Enemy)donor.Models.Enemies.Single(m => m.Name == person.Host).DeepCopy();
            if (listed.SibPath?.Contains(person.Host, StringComparison.Ordinal) != true) throw new InvalidDataException("Host model entry does not name its character in its path: " + person.Host);
            listed.Name = identity; listed.SibPath = listed.SibPath.Replace(person.Host, identity, StringComparison.Ordinal);
            spoken.Models.Enemies.Add(listed);
            // lazy: the source's human rows carry no body size (humans take theirs elsewhere); a person keeps the host template's.
            (float Height, float Radius)? size = bodySizes.TryGetValue(placed.NPCParamID, out var read) ? read : null;
            int friend = MSBE.Read(archive.Read($"map/mapstudio/{person.FriendMap}.msb.dcx")).Parts.Enemies.SingleOrDefault(p => p.Name == person.FriendPart)?.NPCParamID
                ?? throw new InvalidDataException($"Host map {person.FriendMap} has no placement {person.FriendPart}");
            characterRows.Add((template.NPCParamID / 10000 * 10000 + FirstCharacterRow + characterRows.Count, template.NPCParamID, $"aog_{name}", size, null, friend, null));
            int stoodOn = Array.IndexOf(terrain.Sources, placed.CollisionName);
            if (stoodOn < 0) throw new InvalidDataException($"Source placement {placed.Name} stands on a collision that is not built: {placed.CollisionName}");
            string floor = $"h{terrain.Low[stoodOn].Model}_0000";
            int said = peopleTalk.Count + 1;
            peopleTalk.Add(("aog_" + placed.Name, KeeperTalk + said, KeeperAction + said, KeeperMessage + said, SourceTalkReader.OpeningLine(source, "m10_02_00_00", placed.TalkID)));
            FirelinkMapWriter.AddEnemy(spoken, template, "aog_" + placed.Name, characterRows[^1].Row, template.ThinkParamID, floor, placed.Position + terrain.Offset, placed.Rotation, peopleTalk[^1].Script, identity);
            // A person stands as it starts in the source: the animation its placement names, or the human's plain idle.
            people.Add(new { name = "aog_" + placed.Name, source = placed.Name, body = identity, pose = Math.Max(0, placed.InitAnimID), row = characterRows[^1].Row, behaviour = template.ThinkParamID, collision = floor,
                parts = peoplePieces[name].Select(piece => piece.Stem) });
        }
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
        foreach (var talk in peopleTalk)
            talkBundle.Files.Add(new(talkFlags, talkBundle.Files.Max(f => f.ID) + 1, $"{talkFolder}t{talk.Script:D9}.esd", TalkScript.OneMessage(talk.Action, talk.Message)));
        files.Add("mod/" + talkPath, talkBundle.Write());
        // Her prompt and response are the source's own words, added to the host's newest menu text bundle.
        const string hostText = "msg/engus/menu_dlc02.msgbnd.dcx";
        var sourceText = source.Read("msg/ENGLISH/menu.msgbnd.dcx");
        files.Add("mod/" + hostText, MessageBundle.Add(archive.Read(hostText), [
            ("ActionButtonText.fmg", KeeperPrompt, MessageBundle.ReadSource(sourceText, "Event_text_.fmg", SourceKeeperPrompt)),
            ("EventTextForTalk.fmg", KeeperMessage, MessageBundle.ReadSource(sourceText, "Event_text_.fmg", SourceKeeperResponse)),
            .. peopleTalk.Select(talk => ("EventTextForTalk.fmg", talk.Message, talk.Line))]));
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
        // The host composes a frame from its accumulated diffuse, specular and emitted light. The build's scenery carries the
        // source's lighting as marked emitted light, which the stage now shows alone.
        const string rendering = "shader/GXRendererShader.shaderbnd.dcx", compositeStage = "GXLightComposite.ppo";
        var renderingArchive = archive.Read(rendering);
        var compositeProgram = ShaderArchive.Read(renderingArchive, compositeStage);
        files.Add("mod/" + rendering, ShaderArchive.Replace(renderingArchive, [compositeStage],
            compiler.Assemble(CompositeShader.KeepSourceLight(compiler.Disassemble(compositeProgram)), compositeProgram)));
        string graphics = $"param/drawparam/{Map[..6]}_{Map[7..9]}00.gparam.dcx";
        files.Add("mod/" + graphics, HostAtmosphere.RemoveDirectionalLights(HostAtmosphere.RemoveHostEffects(archive.Read(graphics))));

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
        // A living character's colour is read by the host's own character program, which expects display-encoded colour.
        var pieceMeshes = peoplePieces.Values.SelectMany(pieces => pieces.SelectMany(piece => piece.Meshes)).ToArray();
        var displayColours = livingModels.Values.SelectMany(m => m.Meshes.Select(x => x.Colour)).Concat(pieceMeshes.Select(m => m.Colour)).ToHashSet(StringComparer.Ordinal);
        // A character's metal map and its relief map with gloss are made from the source's specular and relief maps; they are
        // named by what they are made from.
        static string MetalOf(string specular) => specular == "" ? NeutralTextures.Neutral : "aog:metal:" + specular;
        static string ReliefOf(string relief, string specular) => $"aog:relief:{relief}:{specular}";
        var characterMaps = livingModels.Values.SelectMany(m => m.Meshes.Select(x => (x.Relief, x.Specular))).Concat(pieceMeshes.Select(m => (m.Relief, m.Specular))).Distinct().ToArray();
        var characterInputs = characterMaps.SelectMany(m => new[] { m.Relief, m.Specular }).Where(n => n != "" && !NeutralTextures.Is(n)).Distinct(StringComparer.Ordinal).ToArray();
        var livingTextures = displayColours.Concat(characterMaps.Select(m => MetalOf(m.Specular))).Concat(characterMaps.Select(m => ReliefOf(m.Relief, m.Specular))).Append(NeutralTextures.Neutral).Distinct(StringComparer.Ordinal).ToArray();
        if (models.SelectMany(m => m.Textures).Intersect(displayColours).FirstOrDefault() is { } shared) throw new InvalidDataException("A texture is read both as scenery and as a living character's colour: " + shared);
        var required = models.SelectMany(m => m.Textures).Concat(environments.Values).Concat(livingModels.Count == 0 ? [] : livingTextures)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var environmentBinder = BND4.Read(archive.Read($"map/m10/{Map}/{Map}_envmap_00_high_00.tpfbnd.dcx"));
        var textureTemplate = environmentBinder.Files.Select(f => DCX.Is(f.Bytes) ? DCX.Decompress(f.Bytes) : f.Bytes)
            .FirstOrDefault(b => b.Length > 36 && b[25] == 1 && new DDS(TextureReader.ReadPackage(b).First().Bytes).GetDXGIFormat() == DDS.DXGI_FORMAT.BC6H_UF16)
            ?? throw new InvalidDataException("Host install lacks an HDR cube texture template");
        // Texture packages are stored compressed like the host's other loose map files.
        DCX.Decompress(archive.Read($"map/m10/{Map}/{Map}_000000.mapbnd.dcx"), out var packageCompression);
        var textureWriter = new TextureWriter(textureTemplate, packageCompression);
        // An object's textures come from its own binder; every other name is looked up in the map's packages.
        // A living character may wear textures held by another character of its family, named in the texture's own name.
        var textureHolders = livingCharacters.Keys.Concat(displayColours.Concat(characterInputs).Select(t => Regex.Match(t, @"^c\d{4}")).Where(m => m.Success).Select(m => m.Value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var held = sceneryObjects.SelectMany(reader.ReadObjectTextures).Concat(sceneryCharacters.Keys.Concat(textureHolders).SelectMany(reader.ReadCharacterTextures)).ToArray();
        var raw = held.Where(t => required.Contains(t.Name)).DistinctBy(t => t.Name, StringComparer.Ordinal).ToDictionary(t => t.Name, t => t.Bytes, StringComparer.Ordinal);
        // A person's textures come with the part models that wear them, or from the body textures all humans share.
        var partTextures = peoplePieces.Values.SelectMany(pieces => pieces.SelectMany(piece => piece.Textures)).Concat(peoplePieces.Count == 0 ? [] : TextureReader.ReadPackage(source.Read("parts/Common_Body.tpf")))
            .DistinctBy(t => t.Name, StringComparer.Ordinal).ToDictionary(t => t.Name, t => t.Bytes, StringComparer.Ordinal);
        foreach (string name in pieceMeshes.Select(m => m.Colour).Distinct(StringComparer.Ordinal).Where(n => !raw.ContainsKey(n) && !NeutralTextures.Is(n)))
            raw.Add(name, partTextures.TryGetValue(name, out var bytes) ? bytes : throw new InvalidDataException("Source part texture is missing: " + name));
        // The maps a character's metal and relief are made from: held with the character, with a person's parts, or in the map's packages.
        var inputs = held.Where(t => characterInputs.Contains(t.Name)).DistinctBy(t => t.Name, StringComparer.Ordinal).ToDictionary(t => t.Name, t => t.Bytes, StringComparer.Ordinal);
        foreach (string name in characterInputs.Where(n => !inputs.ContainsKey(n) && partTextures.ContainsKey(n))) inputs.Add(name, partTextures[name]);
        foreach (var texture in TextureReader.Read(source, characterInputs.Where(n => !inputs.ContainsKey(n)).ToArray())) inputs.Add(texture.Name, texture.Bytes);
        byte[] Input(string name) => NeutralTextures.Is(name) ? NeutralTextures.Bytes(name) : inputs.TryGetValue(name, out var found) ? found : throw new InvalidDataException("Source character texture is missing: " + name);
        // The share of each specular map that is metal; a surface with no specular map has none.
        var metalness = new Dictionary<string, float>(StringComparer.Ordinal) { [""] = 0 };
        foreach (var (relief, specular) in characterMaps)
        {
            if (specular != "") { raw.TryAdd(MetalOf(specular), HostCharacterTextures.Metal(Input(specular))); metalness.TryAdd(specular, HostCharacterTextures.Metalness(Input(specular))); }
            raw.TryAdd(ReliefOf(relief, specular), HostCharacterTextures.ReliefAndGloss(Input(relief), specular == "" ? null : Input(specular)));
        }
        // The cube that lights characters is read in the same pass; it is written only if scenery needs it too.
        foreach (var texture in TextureReader.Read(source, required.Append(characterCubeName).Append(characterSkyName).Where(n => !NeutralTextures.Is(n) && !raw.ContainsKey(n)).Distinct(StringComparer.Ordinal).ToArray())) raw.Add(texture.Name, texture.Bytes);
        var characterCube = BlockTexture.DecodeRadiance(raw[characterCubeName]);
        var characterLight = SourceCharacterLight.Fit(characterCube, CharacterColour("gFC_EnvDifMapMulCol").Select(v => v * environmentSettings.Strength).ToArray(),
            CharacterColour("gFC_HemAmbCol_u"), CharacterColour("gFC_HemAmbCol_d"));
        // The host keeps the baked light once for each state of the sky and each quality setting.
        var characterVolumes = new List<string>();
        for (int state = 0; state < 100; state++)
            foreach (string tier in new[] { "high", "low", "middle" })
            {
                string path = $"map/m10/{Map}/{Map}_envmap_{state:D2}_{tier}_00.tpfbnd.dcx";
                if (!archive.Contains(path)) continue;
                files.Add("mod/" + path, HostIrradiance.Uniform(archive.Read(path), characterLight, raw[characterSkyName]));
                characterVolumes.Add(path);
            }
        if (characterVolumes.Count == 0) throw new InvalidDataException("Host map has no baked environment light to replace");
        foreach (string neutral in required.Where(NeutralTextures.Is)) raw.Add(neutral, NeutralTextures.Bytes(neutral));
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (required.Length > 1000) throw new InvalidDataException("Texture namespace exhausted");
        for (int i = 0; i < required.Length; i++)
        {
            // lazy: one reserved namespace of a thousand textures; split across namespaces when a larger area needs more.
            string stem = $"aet950_{i:D3}", name = stem.ToUpperInvariant() + "_a";
            foreach (string suffix in new[] { "", "_l" })
                Add($"asset/aet/aet950/{stem}{suffix}.tpf.dcx", textureWriter.Write(name + suffix, raw[required[i]], displayColours.Contains(required[i])));
            names.Add(required[i], name + ".tif");
        }
        // Enemies and people are written whole, as characters of their own. A person's body is the source human's
        // skeleton model under the pieces it wears and holds.
        var wholeHuman = LivingPeople.Count == 0 ? null : ModelReader.Read(BND3.Read(source.Read("chr/c0000.chrbnd.dcx")).Files.SingleOrDefault(f => f.Name.EndsWith("c0000.flver", StringComparison.OrdinalIgnoreCase))?.Bytes
            ?? throw new InvalidDataException("Source install has no human skeleton model")).Item1;
        var wholeCharacters = new SortedDictionary<string, object>(StringComparer.Ordinal); var wholePeople = new SortedDictionary<string, object>(StringComparer.Ordinal);
        // Definitions of the build's own for characters, by name: the host definition each is made from, the colour it
        // multiplies its colour map by, and whether it is cut out.
        var characterDefinitions = new SortedDictionary<string, (string Plain, float[] Tint, bool Cutout)>(StringComparer.Ordinal);
        foreach (string hostName in livingCharacters.Values.Select(l => l.Host).Concat(LivingPeople.Values.Select(l => l.Host)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var binder = BND4.Read(archive.Read($"chr/{hostName}.chrbnd.dcx"));
            var file = binder.Files.SingleOrDefault(f => f.Name.EndsWith(hostName + ".flver", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Host character has no model: " + hostName);
            var hostModel = FLVER2.Read(file.Bytes);
            int Exemplar(string fragment) => hostModel.Meshes.FindIndex(m => hostModel.Materials[m.MaterialIndex].MTD.Contains(fragment, StringComparison.Ordinal)) is >= 0 and var found
                ? found : throw new InvalidDataException($"Host character {hostName} has no material like {fragment}");
            HostCharacterModel.Part Dress(int mesh, int exemplar, string colour, string relief, string specular, bool cutout, (float[] Plain, float[] Metal) tint)
            {
                // The source multiplies the colour map by one colour where the surface is not metal and by another where it
                // is; the host definition has one colour for the whole surface.
                // lazy: the two are mixed by the surface's share of metal; telling them apart texel by texel means writing
                // the multiplied colour map, which needs the colour maps decoded and re-encoded.
                int[] mixed = Enumerable.Range(0, 3).Select(c => (int)MathF.Round(1000 * (tint.Plain[c] + (tint.Metal[c] - tint.Plain[c]) * metalness[specular]))).ToArray();
                if (mixed.Any(v => v is < 0 or > 9999)) throw new InvalidDataException("Source definition's colour is out of range");
                string? definition = !cutout && mixed.All(v => v == 1000) ? null : $"{CharacterDefinition}_{mixed[0]:D4}_{mixed[1]:D4}_{mixed[2]:D4}{(cutout ? "_cutout" : "")}";
                if (definition is not null) characterDefinitions.TryAdd(definition, (hostModel.Materials[hostModel.Meshes[exemplar].MaterialIndex].MTD, mixed.Select(v => v / 1000f).ToArray(), cutout));
                var inputs = hostModel.Materials[hostModel.Meshes[exemplar].MaterialIndex].Textures.Select(t => t.ParamName).ToArray();
                string Input(string suffix) => inputs.Count(i => i.EndsWith(suffix, StringComparison.Ordinal)) == 1 ? inputs.Single(i => i.EndsWith(suffix, StringComparison.Ordinal))
                    : throw new InvalidDataException($"Host character material does not have one input ending {suffix}");
                return new(mesh, exemplar, new Dictionary<string, string> { [Input(ColourInput)] = names[colour], [Input(ReliefInput)] = names[ReliefOf(relief, specular)], [Input(MetalInput)] = names[MetalOf(specular)] },
                    definition);
            }
            foreach (var (name, person) in LivingPeople.Where(p => p.Value.Host == hostName))
            {
                var placed = sourceLayout.Parts.Enemies.Single(p => p.Name == name);
                var pieces = peoplePieces[name].Select(piece =>
                {
                    var parts = piece.Meshes.Select(mesh => Dress(mesh.Mesh, Exemplar(piece.Hand == "" ? person.Body : person.Held), mesh.Colour, mesh.Relief, mesh.Specular, mesh.Cutout, mesh.Tint)).ToArray();
                    // What is held hangs from the hand's weapon bone: every node of its model is carried there.
                    var pairs = piece.Hand == "" ? null : piece.Model.Nodes.Select(n => n.Name).Distinct(StringComparer.Ordinal).ToDictionary(n => n, _ => piece.Hand, StringComparer.Ordinal);
                    return new HostCharacterModel.Body(piece.Model, pairs, parts, null, null, 1);
                }).ToArray();
                string identity = PersonIdentity(placed.NPCParamID); int pose = Math.Max(0, placed.InitAnimID);
                var whole = WholeCharacter.WritePerson(source, archive, identity, new(hostName, person.Physics, person.Gaze), wholeHuman!, pieces, pose);
                foreach (var (path, bytes) in whole.Files) files.Add("mod/" + path, bytes);
                wholePeople.Add(name, new { identity, behaviour = hostName, physics = person.Physics, bones = whole.Bones, pose, standing = whole.Stood, events = new { kept = whole.Events.Kept, dropped = whole.Events.Dropped },
                    bodies = whole.Physics.Bodies, joints = whole.Physics.Joints });
            }
            // An enemy has files of its own, under its own identity; its meshes take this host's layouts and materials.
            foreach (var (name, living) in livingCharacters.Where(c => c.Value.Host == hostName))
            {
                var parts = livingModels[name].Meshes.Select(mesh => Dress(mesh.Mesh, Exemplar(mesh.Mark == "" ? living.Body : living.Held), mesh.Colour, mesh.Relief, mesh.Specular, mesh.Cutout, mesh.Tint)).ToArray();
                var whole = WholeCharacter.Write(source, archive, name, Identity(name), new(hostName, living.Physics, living.Gaze), livingModels[name].Model, parts);
                foreach (var (path, bytes) in whole.Files) files.Add("mod/" + path, bytes);
                wholeCharacters.Add(name, new
                {
                    identity = Identity(name), behaviour = hostName, physics = living.Physics, bones = whole.Bones, animations = whole.Animations, standing = whole.Stood,
                    events = new { kept = whole.Events.Kept, dropped = whole.Events.Dropped },
                    bodies = whole.Physics.Bodies, boxesAsCapsules = whole.Physics.Boxes, joints = whole.Physics.Joints, breakableJointsHeld = whole.Physics.Held, jointValuesKept = whole.Physics.Kept,
                });
            }
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
        // The characters' cut-out definition: the plain host definition their meshes are laid out for, with a coverage threshold.
        if (characterDefinitions.Values.Select(d => d.Plain).Distinct(StringComparer.Ordinal).Count() > 1) throw new InvalidDataException("Character meshes are laid out for more than one host definition");
        foreach (var (name, (plainPath, tint, cutout)) in characterDefinitions)
        {
            string stem = Path.GetFileNameWithoutExtension(plainPath.Replace('\\', '/'));
            var plainDefinition = definitions.Files.SingleOrDefault(f => Path.GetFileNameWithoutExtension(f.Name.Replace('\\', '/')).Equals(stem, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Host install has no material definition " + stem);
            string ownName = plainDefinition.Name[..^Path.GetFileName(plainDefinition.Name.Replace('\\', '/')).Length] + name + ".matbin";
            if (definitions.Files.Any(f => f.Name.Equals(ownName, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("A character definition of the build's own is already installed: " + name);
            var own = HostCharacterMaterial.Tinted(plainDefinition.Bytes, tint);
            definitions.Files.Add(new BinderFile(plainDefinition.Flags, definitions.Files.Max(f => f.ID) + 1, ownName, cutout ? HostCharacterMaterial.Cutout(own, CutoutReference) : own));
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
        // Each read opens the whole installed archive, so a stage is read once however many families are made from it.
        var hostStages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[] HostStage(string suffix) => hostStages.TryGetValue(suffix, out var read) ? read : hostStages[suffix] = ShaderArchive.Read(shaders, HostFamily + ".shaderbdle", HostFamily + suffix);
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

        // Navigation: each source mesh becomes a host mesh, written into the first installed mesh file that has faces and
        // numbered after the built collision it lies on, as the host pairs its own; the map's index lists them, the edges
        // along which they join, and the gates between them.
        string meshBundlePath = $"map/m10/{Map}/{Map}.nvmhktbnd.dcx", meshIndexPath = $"map/m10/{Map}/{Map}.nva.dcx";
        var meshBundle = BND4.Read(archive.Read(meshBundlePath));
        var meshTemplate = meshBundle.Files.OrderBy(f => f.ID).FirstOrDefault(f => HostNavigationMesh.IsTemplate(f.Bytes))
            ?? throw new InvalidDataException("Host map has no navigation mesh to build on");
        var meshName = Regex.Match(meshTemplate.Name, @"^(.*_)\d{6}\.hkx$");
        if (!meshName.Success) throw new InvalidDataException("Unclassified host navigation mesh name");
        // The source keeps its walkable mesh about half a metre inside the walls; the host wants the floor itself and allows
        // for a character's width when it searches, so the border is moved back out over the build's own collision.
        var solid = terrain.Low.SelectMany(p => p.Geometry.Triangles.Select(t => (p.Geometry.Vertices[t.A], p.Geometry.Vertices[t.B], p.Geometry.Vertices[t.C]))).ToArray();
        var widened = NavigationBorder.Widen(walkable.Vertices.Select(v => v + terrain.Offset).ToArray(), walkable.Triangles, solid, WalkableReach);
        var walkPieces = NavigationPieces.Split(widened.Vertices, walkable.Triangles, walkable.Parts);
        // A source mesh lies on the source collision of the same number.
        int[] meshModels = walkPieces.Pieces.Select(piece =>
        {
            int built = piece.Part.StartsWith('n') ? Array.IndexOf(terrain.Sources, "h" + piece.Part[1..]) : -1;
            if (built < 0) throw new InvalidDataException("Source navigation mesh lies on no built collision: " + piece.Part);
            return int.Parse(terrain.Low[built].Model, System.Globalization.CultureInfo.InvariantCulture);
        }).ToArray();
        int firstMeshId = meshBundle.Files.Min(f => f.ID);
        meshBundle.Files = walkPieces.Pieces.Select((piece, i) => new BinderFile(meshTemplate.Flags, firstMeshId + i, $"{meshName.Groups[1].Value}{meshModels[i]:D6}.hkx",
            HostNavigationMesh.Write(meshTemplate.Bytes, piece.Vertices, piece.Triangles))).ToList();
        files.Add("mod/" + meshBundlePath, meshBundle.Write());
        var meshIndex = DCX.Decompress(archive.Read(meshIndexPath), out var meshIndexCompression);
        files.Add("mod/" + meshIndexPath, DCX.Compress(HostNavigationIndex.Write(meshIndex, meshModels, walkPieces), meshIndexCompression));

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
        // Each person's talk takes the same prompt wording as the Fire Keeper's.
        foreach (var talk in peopleTalk) actions.Bytes = GameplayTables.AddInteraction(actions.Bytes, HostTalkAction, talk.Action, talk.Name, KeeperPrompt);
        var characterTable = regulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "NpcParam.param");
        foreach (var row in characterRows) characterTable.Bytes = GameplayTables.AddCharacter(characterTable.Bytes, row.Template, row.Row, row.Name, row.Body, row.Mark, row.Standing, row.Variation);
        if (behaviourRows.Count > 0)
        {
            var behaviourTable = regulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "BehaviorParam.param");
            behaviourTable.Bytes = GameplayTables.AddBehaviours(behaviourTable.Bytes, behaviourRows.Select(r => (r.Key, r.Value)).ToArray());
            var attackTable = regulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "AtkParam_Npc.param");
            foreach (var like in attackRows.GroupBy(r => r.Value.Template).OrderBy(g => g.Key))
                attackTable.Bytes = GameplayTables.AddAttacks(attackTable.Bytes, like.Key, like.Select(r => (r.Key, r.Value.Row)).ToArray());
        }
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
            characterLight = new { row = characterRow.Id, constant = characterLight.Constant, x = characterLight.X, y = characterLight.Y, z = characterLight.Z, sky = characterSkyName, reflection = reflectionGain[0] * ReflectionScale, volumes = characterVolumes },
            unbuiltSurfaces = unbuilt,
            unbuiltObjects,
            unbuiltCharacters,
            enemies,
            wholeCharacters,
            wholePeople,
            attacks = new { behaviours = behaviourRows.Keys, hits = attackRows.Keys, projectiles = attacksOf.Values.Sum(a => a.Projectiles) },
            people,
            characters = characterSupports.ToDictionary(c => c.Key, c => new { collision = c.Value, rows = selections[c.Value] }),
            navigation = new { vertices = walkable.Vertices.Length, faces = walkable.Triangles.Length, dropped = walkable.Dropped,
                border = new { reach = WalkableReach, moved = widened.Moved, furthest = widened.Furthest }, joins = walkPieces.Joins.Length,
                pieces = walkPieces.Pieces.Select((piece, i) => new { part = piece.Part, model = meshModels[i], vertices = piece.Vertices.Length, faces = piece.Triangles.Length, gates = piece.Gates.Length }) },
            talk = new { fireKeeper = new { entity = keeperEntity, script = KeeperTalk, action = KeeperAction, prompt = KeeperPrompt, message = KeeperMessage },
                people = peopleTalk.Select(t => new { name = t.Name, script = t.Script, action = t.Action, prompt = KeeperPrompt, message = t.Message }) },
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
