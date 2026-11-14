using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgeOfGold;
using AgeOfGold.Building;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class FirelinkBuildInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks must never run in CI");
        output = Install.Canonical(output);
        if (!Install.Within(output, Install.Canonical(Path.GetTempPath())) || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("Fresh build check output under system temp required");
        string source = Required("AOG_DARK_SOULS_REMASTERED"), host = Required("AOG_ELDEN_RING");
        Oodle.SearchPaths.Add(host);
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        string first = Path.Combine(output, "first"), repeat = Path.Combine(output, "repeat");
        Directory.CreateDirectory(output);
        string toolchain = Required("AOG_LLVM_MINGW");
        FirelinkBuild.Run(source, host, first, compiler, toolchain);
        FirelinkBuild.Run(source, host, repeat, compiler, toolchain);
        string[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        var files = Files(first);
        if (!files.SequenceEqual(Files(repeat))) throw new Exception("Independent builds contain different files");
        foreach (var file in files)
            if (!File.ReadAllBytes(Path.Combine(first, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(repeat, file))))
                throw new Exception("Independent builds differ: " + file);

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(first, "manifest.json")));
        var recorded = manifest.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(f => f.GetProperty("path").GetString()!, f => f.GetProperty("sha256").GetString()!);
        if (!recorded.Keys.Order(StringComparer.Ordinal).SequenceEqual(files.Where(f => f != "manifest.json")))
            throw new Exception("Manifest does not list exactly the written files");
        foreach (var (path, hash) in recorded)
            if (Hash(File.ReadAllBytes(Path.Combine(first, path))) != hash) throw new Exception("Manifest fingerprint differs: " + path);

        // The host waits indefinitely on a file named as compressed that is not.
        foreach (var file in files.Where(f => f.EndsWith(".dcx", StringComparison.Ordinal)))
        {
            using var stream = File.OpenRead(Path.Combine(first, file));
            var magic = new byte[4]; stream.ReadExactly(magic);
            if (!magic.AsSpan().SequenceEqual("DCX\0"u8)) throw new Exception("Built file is named compressed but is not: " + file);
        }
        string mod = Path.Combine(first, "mod");
        var layout = MSBE.Read(Path.Combine(mod, "map/mapstudio/m10_01_00_00.msb.dcx"));
        var pieces = layout.Parts.MapPieces.Where(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable).ToArray();
        if (pieces.Length == 0 || pieces.Any(p => !p.ModelName.StartsWith("m81", StringComparison.Ordinal))
            || layout.Parts.Collisions.Count(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable) != 39
            || layout.Parts.Enemies.Count(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable) != 2)
            throw new Exception("Built map retains host placements or lacks source terrain and scenery");
        // A resting point: marker, controller and respawn region placed together and registered with the build's flag.
        var rest = manifest.RootElement.GetProperty("restingPoint");
        uint restEntity = rest.GetProperty("entity").GetUInt32(); int restFlag = rest.GetProperty("flag").GetInt32();
        var restMarker = layout.Parts.Assets.Single(p => p.EntityID == restEntity);
        var script = EMEVD.Read(Path.Combine(mod, "event/m10_01_00_00.emevd.dcx"));
        if (restMarker.GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable || layout.Parts.Enemies.Count(p => p.EntityID == restEntity - 1000) != 1
            || layout.Regions.SpawnPoints.Count(r => r.EntityID == restEntity + 1000 && System.Numerics.Vector3.Distance(r.Position, restMarker.Position) < 5) != 1
            || script.Events.Single(e => e.ID == 0).Instructions.Count(i => i.Bank == 2009 && i.ID == 3 && BitConverter.ToInt32(i.ArgData, 0) == restFlag
                && BitConverter.ToUInt32(i.ArgData, 4) == restEntity) != 1)
            throw new Exception("Built resting point lacks its marker, controller, respawn region or registration");
        // The source's own bonfire model stands at the resting point as scenery; other source objects are recorded as not built.
        if (pieces.Count(p => p.Position == restMarker.Position) != 1
            || manifest.RootElement.GetProperty("unbuiltObjects").EnumerateObject().Any(o => o.Name == "o0200" || o.Value.GetInt32() < 1))
            throw new Exception("Built map lacks the source bonfire model at its resting point");
        // The bonfire's fire: a translated effect in the host's bundle with its textures, placed just above the model.
        var burning = manifest.RootElement.GetProperty("effects").GetProperty("bonfire");
        int fireId = burning.GetProperty("effect").GetInt32();
        var fireRegion = layout.Regions.SFX.Single();
        var effectBundle = BND4.Read(Path.Combine(mod, "sfx/sfxbnd_commoneffects.ffxbnd.dcx"));
        var fireEffect = FXR3.Read(effectBundle.Files.Single(f => f.Name.EndsWith($"f{fireId:D9}.fxr", StringComparison.Ordinal)).Bytes);
        var fireTextures = fireEffect.RootContainer.Containers.Select(c => (int)((FXR3.Field.FieldFloat)c.Effects.Single().Actions[9].Properties1[0].Fields[0]).Value).Distinct().Order().ToArray();
        if (fireRegion.EffectID != fireId || fireEffect.Id != fireId || fireEffect.RootContainer.Containers.Count != burning.GetProperty("clusters").GetInt32()
            || fireEffect.RootContainer.Containers.Count < 2 || System.Numerics.Vector3.Distance(fireRegion.Position, restMarker.Position) is not (> 0.05f and < 0.5f)
            || fireRegion.Position.Y <= restMarker.Position.Y
            || !fireTextures.SequenceEqual(burning.GetProperty("textures").EnumerateObject().Select(t => t.Value.GetInt32()).Order())
            || fireTextures.Any(id => effectBundle.Files.Count(f => f.Name.EndsWith($"\\s{id}_a.tpf", StringComparison.Ordinal)) != 1)
            || System.Text.Encoding.ASCII.GetString(effectBundle.Files.Single(f => f.Name.EndsWith($"f{fireId:D9}.ffxreslist", StringComparison.Ordinal)).Bytes)
                != string.Concat(fireTextures.Select(id => $"s{id}_a.tif\r\n")))
            throw new Exception("Built bonfire fire lacks its effect, textures, texture list or placement");
        // Its particles are marked, and the host particle stage answers the marker with the source's response.
        var particleStage = compiler.Disassemble(ShaderArchive.Read(File.ReadAllBytes(Path.Combine(mod, "shader/GXFfxShader.shaderbnd.dcx")), "GXFfxtessellateBlendTexture.ppo"));
        if (!particleStage.Contains("%aog_marked = fcmp oeq float %aog_bias, 3.276900e+04", StringComparison.Ordinal)
            || fireEffect.RootContainer.Containers.Any(c => BitConverter.SingleToInt32Bits(32769f * 255) != (c.Effects.Single().Actions[9].Properties1[10].Fields[0] is FXR3.Field.FieldInt whole
                ? whole.Value : BitConverter.SingleToInt32Bits(((FXR3.Field.FieldFloat)c.Effects.Single().Actions[9].Properties1[10].Fields[0]).Value))))
            throw new Exception("Built fire particles are not marked for, or the particle stage lacks, the source's colour response");
        // The Fire Keeper is posed scenery, lit by the rows of the collision she sits on rather than her placement's own.
        var keepers = manifest.RootElement.GetProperty("characters").EnumerateObject().ToArray();
        var sceneryNames = manifest.RootElement.GetProperty("scenery").GetProperty("placements").EnumerateObject().Select(p => p.Value.GetString()).ToArray();
        if (keepers.Length != 1 || !sceneryNames.Contains(keepers[0].Name) || !keepers[0].Value.GetProperty("collision").GetString()!.StartsWith('h')
            || keepers[0].Value.GetProperty("rows").GetProperty("Light").GetInt32() <= 0
            || manifest.RootElement.GetProperty("unbuiltCharacters").EnumerateObject().Any(c => c.Name == "c2750"))
            throw new Exception("Built scenery lacks the posed Fire Keeper or her collision's lighting rows");
        // She is spoken to through a controller whose talk script opens the source's own response.
        var speech = manifest.RootElement.GetProperty("talk").GetProperty("fireKeeper");
        int talkScript = speech.GetProperty("script").GetInt32(), talkAction = speech.GetProperty("action").GetInt32();
        var talker = layout.Parts.Enemies.Single(p => p.EntityID == speech.GetProperty("entity").GetUInt32());
        var talkFile = BND4.Read(Path.Combine(mod, "script/talk/m10_01_00_00.talkesdbnd.dcx")).Files.Single(f => f.Name.EndsWith($"\\m10_01_00_00\\t{talkScript:D9}.esd", StringComparison.Ordinal));
        var talkStates = ESD.Read(talkFile.Bytes).StateGroups[1];
        var sourceWords = BND3.Read(Path.Combine(source, "msg/ENGLISH/menu.msgbnd.dcx")).Files.Where(f => f.Name.EndsWith("Event_text_.fmg", StringComparison.Ordinal)).Select(f => FMG.Read(f.Bytes)).ToArray();
        var hostWords = BND4.Read(Path.Combine(mod, "msg/engus/menu_dlc02.msgbnd.dcx")).Files.ToDictionary(f => f.Name.Split('\\')[^1], f => FMG.Read(f.Bytes));
        string builtPrompt = hostWords["ActionButtonText.fmg"][speech.GetProperty("prompt").GetInt32()], builtResponse = hostWords["EventTextForTalk.fmg"][speech.GetProperty("message").GetInt32()];
        var actionRows = PARAM.Read(AgeOfGold.Archives.Regulation.Read(File.ReadAllBytes(Path.Combine(host, "eldenring.exe")), File.ReadAllBytes(Path.Combine(mod, "regulation.bin")))
            .Tables.Files.Single(f => f.Name.EndsWith("ActionButtonParam.param", StringComparison.Ordinal)).Bytes);
        if (talker.TalkID != talkScript || talker.GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable || talkStates.Count != 6
            || !talkStates[2].Conditions.Single().Evaluator.AsSpan().IndexOf(BitConverter.GetBytes(talkAction)).Equals(2)
            || string.IsNullOrWhiteSpace(builtPrompt) || string.IsNullOrWhiteSpace(builtResponse)
            || !sourceWords.Any(t => t.Entries.Any(e => e.Text == builtPrompt)) || !sourceWords.Any(t => t.Entries.Any(e => e.Text == builtResponse))
            || actionRows.Rows.Count(r => r.ID == talkAction) != 1)
            throw new Exception("Built Fire Keeper talk lacks its controller, script, source words or action");
        // Navigation: one mesh of the source's walkable triangles, listed alone in the map's index.
        var walk = manifest.RootElement.GetProperty("navigation");
        var meshes = BND4.Read(Path.Combine(mod, "map/m10/m10_01_00_00/m10_01_00_00.nvmhktbnd.dcx")).Files;
        var builtMesh = new AgeOfGold.Collision.SourceTagfile(meshes.Single().Bytes, "20180100").Root.Field("namedVariants").Elements()[0].Field("variant");
        var builtIndex = DCX.Decompress(File.ReadAllBytes(Path.Combine(mod, "map/m10/m10_01_00_00/m10_01_00_00.nva.dcx")));
        int walkFaces = walk.GetProperty("faces").GetInt32();
        if (walkFaces < 1000 || builtMesh.Field("faces").Elements().Length != walkFaces || builtMesh.Field("vertices").Elements().Length != walk.GetProperty("vertices").GetInt32()
            || !meshes.Single().Name.EndsWith($"_{walk.GetProperty("model").GetInt32():D6}.hkx", StringComparison.Ordinal)
            // The index opens with a 16-byte header and a 32-byte marker section; the mesh list's header and its one entry follow.
            || BitConverter.ToInt32(builtIndex, 48) != 0 || BitConverter.ToInt32(builtIndex, 48 + 12) != 1 || BitConverter.ToInt32(builtIndex, 64 + 52) != walk.GetProperty("model").GetInt32()
            || BitConverter.ToInt32(builtIndex, 64 + 64) != walkFaces)
            throw new Exception("Built navigation lacks its one mesh or its index entry");
        var materials = BND4.Read(Path.Combine(mod, "material/allmaterial.matbinbnd.dcx")).Files
            .ToLookup(f => string.Join('/', f.Name.Replace('\\', '/').Split('/')[^3..]).ToLowerInvariant(), f => f.Bytes);
        var shaders = File.ReadAllBytes(Path.Combine(mod, "shader/shaderbdle.shaderbdlebnd.dcx"));
        int surfaces = 0, cutouts = 0, blends = 0, scrolling = 0; var families = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in pieces.Select(p => p.ModelName).Distinct())
        {
            var binder = BND4.Read(Path.Combine(mod, $"map/m10/m10_01_00_00/m10_01_00_00_{model[1..]}.mapbnd.dcx"));
            var flver = FLVER2.Read(binder.Files.Single(f => f.Name.EndsWith(".flver", StringComparison.Ordinal)).Bytes);
            foreach (var mesh in flver.Meshes)
            {
                var material = flver.Materials[mesh.MaterialIndex];
                // The host resolves "<set>/<name>.mtd" to the definition stored as "<set>/matxml/<name>.matbin".
                var mtd = material.MTD.Replace('\\', '/').Split('/');
                var matches = materials[$"{mtd[^2]}/matxml/{Path.GetFileNameWithoutExtension(mtd[^1])}.matbin".ToLowerInvariant()].ToArray();
                if (matches.Length != 1) throw new Exception("Built model material does not resolve to one definition: " + material.MTD);
                var definition = MATBIN.Read(matches[0]);
                string family = Path.GetFileNameWithoutExtension(definition.ShaderPath.Replace('\\', '/'));
                bool cut = family.EndsWith("_cutout", StringComparison.Ordinal);
                cutouts += cut ? 1 : 0;
                // A blended surface asks the host for its forward pass and leaves depth alone.
                bool add = family.EndsWith("_add", StringComparison.Ordinal), blend = add || family.EndsWith("_blend", StringComparison.Ordinal);
                blends += blend ? 1 : 0;
                if ((int)definition.Params.Single(p => p.Name == "g_BlendMode").Value != (add ? 4 : blend ? 2 : 0)
                    || blend != (bool)definition.Params.Single(p => p.Name == "g_GX_bDisableDepthWrite").Value)
                    throw new Exception("Built material's blend state disagrees with its shader family: " + family);
                families.Add(family);
                using var bundle = new BND4Reader(ShaderArchive.ReadFamily(shaders, family + ".shaderbdle"));
                var metadata = METAPARAM.Read(bundle.ReadFile(bundle.Files.Single(f => f.Name.EndsWith(".metaparam", StringComparison.Ordinal))));
                if (!metadata.Textures.Select(t => t.Name).Order(StringComparer.Ordinal).SequenceEqual(material.Textures.Select(t => t.ParamName).Order(StringComparer.Ordinal)))
                    throw new Exception("Built model and shader texture declarations disagree");
                foreach (var texture in material.Textures)
                {
                    var match = Regex.Match(texture.Path, @"^(AET\d{3})_(\d{3})_[a-z]\.tif$");
                    if (!match.Success) throw new Exception("Built model references a texture outside the build: " + texture.Path);
                    string stem = (match.Groups[1].Value + "_" + match.Groups[2].Value).ToLowerInvariant();
                    foreach (string suffix in new[] { "", "_l" })
                        if (!File.Exists(Path.Combine(mod, $"asset/aet/{stem[..6]}/{stem}{suffix}.tpf.dcx")))
                            throw new Exception("Built texture package missing: " + stem + suffix);
                }
                surfaces++;
            }
        }
        if (surfaces != 348 || cutouts != 78 || blends != 34) throw new Exception("Built opaque and cutout surface inventory differs");
        var lit = manifest.RootElement.GetProperty("groundFamilies").EnumerateObject().Select(f => f.Name).Order(StringComparer.Ordinal).ToArray();
        if (!lit.SequenceEqual(families.Order(StringComparer.Ordinal))) throw new Exception("Built models do not use exactly the recorded lit ground families");
        foreach (string family in families)
        {
            using var bundle = new BND4Reader(ShaderArchive.ReadFamily(shaders, family + ".shaderbdle"));
            var rows = new List<string>();
            // Deferred surfaces are drawn by the G-buffer stages and blended ones by the forward stages.
            bool blend = family.EndsWith("_blend", StringComparison.Ordinal) || family.EndsWith("_add", StringComparison.Ordinal);
            string pass = blend ? "_Fwd" : "_Gbuf";
            foreach (var stage in bundle.Files.Where(f => Regex.IsMatch(f.Name, pass + @"(@\[cl\])?\.vpo$")))
            {
                var signatures = new ShaderSignatures(compiler.Disassemble(bundle.ReadFile(stage)));
                rows.Add(string.Join(",", new[] { 2, 4, 6 }.Select(index => signatures.Require(true, "TANGENT", index, 9).Row)));
            }
            foreach (string variant in new[] { pass + ".ppo", pass + "_[A].ppo" })
            {
            var pixel = compiler.Disassemble(bundle.ReadFile(bundle.Files.Single(f => f.Name.EndsWith(family + variant, StringComparison.Ordinal))));
            var inputs = new ShaderSignatures(pixel, "ps");
            // Every vertex variant feeds the one pixel stage, so they must agree on the added registers.
            rows.Add(string.Join(",", new[] { 4, 6 }.Select(index => inputs.Find(false, "TANGENT", index).Row).Prepend(inputs.Find(false, "TANGENT", 4).Row - 1)));
            if (rows.Count < 2 || rows.Distinct().Count() != 1) throw new Exception("Built vertex variants and pixel stage disagree on tangent registers: " + string.Join(" | ", rows));
            var declared = METAPARAM.Read(bundle.ReadFile(bundle.Files.Single(f => f.Name.EndsWith(".metaparam", StringComparison.Ordinal))));
            if (pixel.Contains("@dx.op.discard(i32 82, i1 %aog_uncovered)", StringComparison.Ordinal) != family.EndsWith("_cutout", StringComparison.Ordinal))
                throw new Exception("Cutout coverage test is missing or misplaced: " + family);
            if (pixel.Contains("i8 3, float %aog_opacity)", StringComparison.Ordinal) != blend) throw new Exception("Blended opacity output is missing or misplaced: " + family);
            // A surface the source scrolls reads the host's frame clock; a still one never does.
            bool scrolls = manifest.RootElement.GetProperty("groundFamilies").GetProperty(family).GetProperty("scroll").EnumerateArray().Any(v => v.GetSingle() != 0);
            if (pixel.Contains("%aog_clock = ", StringComparison.Ordinal) != scrolls) throw new Exception("Texture scrolling is missing or misplaced: " + family);
            scrolling += scrolls ? 1 : 0;
            if (!pixel.Contains("TextureCube", StringComparison.Ordinal) || (declared.ShaderId & 0x800) == 0)
                throw new Exception("Built ground pixel stage lacks its environment cube or emission writes");
            rows.RemoveAt(rows.Count - 1);
            }
        }
        if (scrolling == 0) throw new Exception("Built families lack the source's scrolling cloud, mist and shaft surfaces");
        var graphics = GPARAM.Read(Path.Combine(mod, "param/drawparam/m10_01_0000.gparam.dcx")).Groups.SelectMany(g => g.Params).ToLookup(p => p.Name1);
        if (graphics["VolumetricFog Enable"].Single().Values.Any(v => (bool)v) || graphics["GS Presents - Vignetting Power"].Single().Values.Any(v => (float)v != 0)
            || graphics["GS Presents - Chromatic Aberration Enable"].Single().Values.Any(v => (int)v != 0))
            throw new Exception("Built graphics parameters keep host effects the source lacks");
        var tables = AgeOfGold.Archives.Regulation.Read(File.ReadAllBytes(Path.Combine(host, "eldenring.exe")), File.ReadAllBytes(Path.Combine(mod, "regulation.bin"))).Tables;
        var effectRows = tables.Files.Single(f => f.Name.EndsWith("SpEffectVfxParam.param", StringComparison.Ordinal)).Bytes;
        int rowCount = BitConverter.ToUInt16(effectRows, 0x0A);
        var leaves = Enumerable.Range(0, rowCount).Select(i => (Id: BitConverter.ToInt32(effectRows, 0x40 + 24 * i), At: BitConverter.ToInt64(effectRows, 0x40 + 24 * i + 8))).Single(r => r.Id == 200000);
        if (BitConverter.ToInt32(effectRows, (int)leaves.At) != -1) throw new Exception("Built regulation still plays the host's drifting leaves");
        if (!File.ReadAllText(Path.Combine(first, "age-of-gold.me3")).Contains("start_online = false", StringComparison.Ordinal))
            throw new Exception("Mod loader profile does not keep the game offline");
        foreach (var (name, root) in new[] { ("source", source), ("host", host) })
            foreach (var input in manifest.RootElement.GetProperty("inputs").GetProperty(name).EnumerateObject())
            {
                var range = Regex.Match(input.Name, @"^(.*)@(\d+)\+(\d+)$");
                byte[] bytes;
                if (range.Success)
                {
                    using var stream = File.OpenRead(Path.Combine(root, range.Groups[1].Value)); stream.Position = long.Parse(range.Groups[2].Value);
                    bytes = new byte[int.Parse(range.Groups[3].Value)]; stream.ReadExactly(bytes);
                }
                else bytes = File.ReadAllBytes(Path.Combine(root, input.Name));
                if (Hash(bytes) != input.Value.GetString()) throw new Exception("Install input changed during the build");
            }
        Directory.Delete(repeat, true);
        Console.WriteLine($"PASS: Firelink build repeats across {files.Length} files; {surfaces} opaque lit surfaces, {pieces.Length} scenery placements and 39 terrain placements read back; installs unchanged");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
