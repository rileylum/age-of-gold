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
            || layout.Parts.Collisions.Count(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable) != 38
            // The enabled characters are the build's two unseen controllers and the enemies and people its manifest lists.
            || layout.Parts.Enemies.Count(p => p.GameEditionDisable == MSBE.Part.GameEditionDisableType.NeverDisable) != 2 + manifest.RootElement.GetProperty("enemies").GetArrayLength() + manifest.RootElement.GetProperty("people").GetArrayLength())
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
        // Living enemies: each source enemy the manifest lists stands in the built map as a character of the build's own,
        // on a built collision, under a build-owned row with the source's body size.
        var enemies = manifest.RootElement.GetProperty("enemies").EnumerateArray().ToArray();
        var sourceEnemies = MSB1.Read(Path.Combine(source, "map/MapStudio/m10_02_00_00.msb")).Parts.Enemies.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var sourceBodies = AgeOfGold.Characters.SourceCharacterReader.BodySizes(new Install(source, "DarkSoulsRemastered.exe"));
        var builtRegulation = AgeOfGold.Archives.Regulation.Read(File.ReadAllBytes(Path.Combine(host, "eldenring.exe")), File.ReadAllBytes(Path.Combine(mod, "regulation.bin")));
        var builtCharacters = PARAM.Read(builtRegulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == "NpcParam.param").Bytes);
        var characterDefinition = new PARAMDEF { ParamType = builtCharacters.ParamType, DataVersion = builtCharacters.ParamdefDataVersion };
        characterDefinition.Fields.Add(new PARAMDEF.Field(characterDefinition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = (int)builtCharacters.DetectedSize });
        builtCharacters.ApplyParamdef(characterDefinition);
        var enemyModels = enemies.Select(e => sourceEnemies[e.GetProperty("source").GetString()!].ModelName).Distinct().ToArray();
        // Every placement of a carried source model is built, and none of those models is still counted as unbuilt.
        if (enemies.Length < 10 || enemies.Length != sourceEnemies.Values.Count(p => enemyModels.Contains(p.ModelName)) || !enemyModels.Contains("c2540") || !enemyModels.Contains("c2900")
            || enemyModels.Any(m => manifest.RootElement.GetProperty("unbuiltCharacters").TryGetProperty(m, out _))) throw new Exception("Built enemies are not one for each placement of the source models carried");
        // Every enemy is moved from its source place by the one displacement the whole map shares.
        var enemyOffset = layout.Parts.Enemies.Single(p => p.Name == enemies[0].GetProperty("name").GetString()).Position - sourceEnemies[enemies[0].GetProperty("source").GetString()!].Position;
        var bodyFiles = new Dictionary<string, FLVER2>(StringComparer.Ordinal);
        // The source collisions the placements of each built body stand on.
        var stoodOn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Stands(string identity, string collision) { if (!stoodOn.TryGetValue(identity, out var on)) stoodOn[identity] = on = new(StringComparer.Ordinal); on.Add(collision); }
        var sourceDefinitions = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var wholeAttacks = new Dictionary<int, AgeOfGold.Characters.SourceCharacterReader.SourceAttacks>();
        var hostArchive = new AgeOfGold.Archives.HostArchive(new Install(host, "eldenring.exe"));
        foreach (var enemy in enemies)
        {
            var placed = layout.Parts.Enemies.Single(p => p.Name == enemy.GetProperty("name").GetString());
            var from = sourceEnemies[enemy.GetProperty("source").GetString()!];
            var rows = builtCharacters.Rows.Where(r => r.ID == placed.NPCParamID).ToArray();
            var raw = rows.Length == 1 ? (byte[])rows[0]["raw"].Value : throw new Exception("A built enemy's row is missing or repeated");
            var size = sourceBodies[from.NPCParamID];
            int shown = BitConverter.ToUInt16(raw, 330);
            if (placed.ModelName != enemy.GetProperty("body").GetString() || placed.Name != "aog_" + from.Name || placed.EntityID != 0 || placed.WalkRouteName is not null
                || placed.GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable || layout.Parts.Collisions.Count(c => c.Name == placed.CollisionPartName) != 1
                || !layout.Models.Enemies.Any(m => m.Name == placed.ModelName) || (placed.Position - enemyOffset - from.Position).Length() > 1e-3f || placed.Rotation != from.Rotation
                || !rows[0].Name.StartsWith("aog_", StringComparison.Ordinal) || BitConverter.ToSingle(raw, 20) != size.Height || BitConverter.ToSingle(raw, 24) != size.Radius
                || BitConverter.ToSingle(raw, 340) != size.Height || BitConverter.ToSingle(raw, 344) != size.Radius)
                throw new Exception($"Built enemy {placed.Name} differs from its source placement, its collision or its row");
            {
                // The enemy is built whole: files of its own under an identity the host lacks, and a row that names the source's behaviour variation.
                string identity = placed.ModelName, sourceModel = from.ModelName;
                // A model placed in a second light is a character of its own, reported under the model's name and a number.
                var report = manifest.RootElement.GetProperty("wholeCharacters").EnumerateObject().Single(c => (c.Name == sourceModel || c.Name.StartsWith(sourceModel + "+", StringComparison.Ordinal))
                    && c.Value.GetProperty("identity").GetString() == identity).Value;
                Stands(identity, from.CollisionName);
                if (report.GetProperty("identity").GetString() != identity || !Regex.IsMatch(identity, @"^c6[4-9]\d\d$") || new[] { "chrbnd", "anibnd", "behbnd" }.Any(kind => hostArchive.Contains($"chr/{identity}.{kind}.dcx") || !File.Exists(Path.Combine(mod, $"chr/{identity}.{kind}.dcx")))
                    || !File.Exists(Path.Combine(mod, $"action/script/{identity}.hks"))
                    // The map lists the model under its own identity, in its name and in its path: without the path the host does not stand the character.
                    || layout.Models.Enemies.Single(m => m.Name == identity).SibPath?.Contains(identity, StringComparison.Ordinal) != true)
                    throw new Exception($"Built enemy {placed.Name} is not a character of the build's own with all its files");
                var attacks = AgeOfGold.Characters.SourceCharacterReader.Attacks(new Install(source, "DarkSoulsRemastered.exe"), from.NPCParamID);
                if (BitConverter.ToInt32(raw, 4) != attacks.Variation) throw new Exception($"Built enemy {placed.Name}'s row does not name the source's behaviour variation");
                wholeAttacks[from.NPCParamID] = attacks;
                if (bodyFiles.ContainsKey(identity)) continue;
                var ownBinder = BND4.Read(Path.Combine(mod, $"chr/{identity}.chrbnd.dcx"));
                var own = bodyFiles[identity] = FLVER2.Read(ownBinder.Files.Single(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                var sourceBody = FLVER2.Read(BND3.Read(Path.Combine(source, $"chr/{sourceModel}.chrbnd.dcx")).Files.Single(f => f.Name.EndsWith(sourceModel + ".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                sourceDefinitions[identity] = sourceBody.Materials.Select(m => Path.GetFileNameWithoutExtension(m.MTD.Replace('\\', '/'))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                // The body stands on the source's own nodes; every texture it names is built; its physics and events are its own.
                if (!own.Nodes.Select(n => n.Name).SequenceEqual(sourceBody.Nodes.Select(n => n.Name)) || own.Dummies.Count != sourceBody.Dummies.Count || own.Meshes.Count == 0 || own.Materials.Any(m => !Regex.IsMatch(m.Name, @"^aog_\d+$"))
                    || own.Materials.Any(m => m.Textures.Count(t => t.Path.StartsWith("AET950_", StringComparison.Ordinal)) != 6)
                    || own.Materials.SelectMany(m => m.Textures).Where(t => t.Path.StartsWith("AET950_", StringComparison.Ordinal)).Select(t => t.Path).Distinct()
                        .Any(t => !File.Exists(Path.Combine(mod, "asset/aet/aet950", Path.GetFileNameWithoutExtension(t)[..^2].ToLowerInvariant() + ".tpf.dcx")))
                    || ownBinder.Files.Count != 2 || report.GetProperty("bodies").GetInt32() < 10 || report.GetProperty("animations").GetInt32() < 100 || report.GetProperty("events").GetProperty("kept").GetInt32() < 100)
                    throw new Exception($"Built enemy body {identity} is not the source's own body with built textures, physics and events");
                var ownEvents = TAE.Read(BND4.Read(Path.Combine(mod, $"chr/{identity}.anibnd.dcx")).Files.Single(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)).Bytes);
                if (ownEvents.ID != 200000 + int.Parse(identity[1..]) || !ownEvents.Animations.Any(a => a.Events.Any(e => e.Type == 1))) throw new Exception($"Built enemy {identity}'s events are not its own, or hold no attack");
                continue;
            }
        }
        // What answers the whole characters' attack events: every behaviour row of their variations, as the source has it, and
        // an attack row for each that one names, with the source's hit sizes, attachment points and damage.
        if (wholeAttacks.Count == 0 || !enemies.Any(e => sourceEnemies[e.GetProperty("source").GetString()!].ModelName == "c2540")) throw new Exception("The hollow soldier is not built whole");
        PARAM Raw(string table)
        {
            var rows = PARAM.Read(builtRegulation.Tables.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == table + ".param").Bytes);
            var definition = new PARAMDEF { ParamType = rows.ParamType, DataVersion = rows.ParamdefDataVersion };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = (int)rows.DetectedSize });
            rows.ApplyParamdef(definition); return rows;
        }
        var builtBehaviours = Raw("BehaviorParam").Rows.ToDictionary(r => r.ID, r => (byte[])r["raw"].Value); var builtAttacks = Raw("AtkParam_Npc").Rows.ToDictionary(r => r.ID, r => (byte[])r["raw"].Value);
        foreach (var attacks in wholeAttacks.Values)
        {
            if (attacks.Behaviours.Length == 0 || attacks.Hits.Length == 0 || attacks.Behaviours.Any(b => !builtBehaviours.TryGetValue(b.Id, out var row) || !row.SequenceEqual(b.Row)))
                throw new Exception("A whole character's behaviour rows are not in the built table as the source has them");
            foreach (var (id, row) in attacks.Hits)
                if (!builtAttacks.TryGetValue(id, out var hit) || !hit.AsSpan(0, 24).SequenceEqual(row.AsSpan(0, 24)) || !hit.AsSpan(44, 16).SequenceEqual(row.AsSpan(44, 16)) || !hit.AsSpan(80, 12).SequenceEqual(row.AsSpan(80, 12)))
                    throw new Exception($"Attack row {id} is missing from the built table or lacks the source's hit");
        }
        if (!manifest.RootElement.GetProperty("attacks").GetProperty("behaviours").EnumerateArray().Select(b => b.GetInt32()).SequenceEqual(wholeAttacks.Values.SelectMany(a => a.Behaviours.Select(b => b.Id)).Distinct().Order()))
            throw new Exception("The manifest does not list the behaviour rows added");
        // People: each source human the manifest lists stands in the built map on a built collision, as a character of the
        // build's own under its own row, with the standing of an installed friendly character and the pose it starts in.
        var people = manifest.RootElement.GetProperty("people").EnumerateArray().ToArray();
        // The two people at Firelink from the start: the cleric by the ruined church and the warrior by the bonfire.
        if (!people.Select(p => p.GetProperty("source").GetString()).Order(StringComparer.Ordinal).SequenceEqual(["c0000_0016", "c0000_0018"]))
            throw new Exception("Built people are not the two who are at Firelink from the start: " + string.Join(", ", people.Select(p => p.GetProperty("source").GetString())));
        var humanFrame = FLVER2.Read(BND3.Read(Path.Combine(source, "chr/c0000.chrbnd.dcx")).Files.Single(f => f.Name.EndsWith("c0000.flver", StringComparison.OrdinalIgnoreCase)).Bytes);
        var peopleBodies = new HashSet<string>(StringComparer.Ordinal);
        // The source's start-up and armour rows, as stored.
        var gameTables = BND3.Read(Path.Combine(source, "param/GameParam/GameParam.parambnd.dcx"));
        Dictionary<int, byte[]> StoredRows(string name)
        {
            var table = PARAM.Read(gameTables.Files.Single(f => f.Name.Replace('\\', '/').EndsWith($"/{name}.param", StringComparison.OrdinalIgnoreCase)).Bytes);
            var whole = new PARAMDEF { ParamType = table.ParamType, DataVersion = table.ParamdefDataVersion };
            whole.Fields.Add(new PARAMDEF.Field(whole, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = (int)table.DetectedSize });
            table.ApplyParamdef(whole);
            return table.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => (byte[])g.First()["raw"].Value);
        }
        var startRows = StoredRows("CharaInitParam"); var armourRows = StoredRows("EquipParamProtector");
        foreach (var person in people)
        {
            var placed = layout.Parts.Enemies.Single(p => p.Name == person.GetProperty("name").GetString());
            var from = sourceEnemies[person.GetProperty("source").GetString()!];
            var raw = (byte[])builtCharacters.Rows.Single(r => r.ID == placed.NPCParamID)["raw"].Value;
            string identity = placed.ModelName;
            var report = manifest.RootElement.GetProperty("wholePeople").GetProperty(from.Name);
            Stands(identity, from.CollisionName);
            if (from.ModelName != "c0000" || placed.Name != "aog_" + from.Name || placed.EntityID != 0 || layout.Parts.Collisions.Count(c => c.Name == placed.CollisionPartName) != 1
                || (placed.Position - enemyOffset - from.Position).Length() > 1e-3f || placed.Rotation != from.Rotation
                // Enemies' rows carry side 6; a person's row does not.
                || raw[307] == 6 || person.GetProperty("parts").GetArrayLength() < 5
                || identity != person.GetProperty("body").GetString() || report.GetProperty("identity").GetString() != identity || !Regex.IsMatch(identity, @"^c6[6-9]\d\d$") || !peopleBodies.Add(identity)
                || person.GetProperty("pose").GetInt32() != Math.Max(0, from.InitAnimID) || report.GetProperty("pose").GetInt32() != Math.Max(0, from.InitAnimID)
                || new[] { "chrbnd", "anibnd", "behbnd" }.Any(kind => hostArchive.Contains($"chr/{identity}.{kind}.dcx") || !File.Exists(Path.Combine(mod, $"chr/{identity}.{kind}.dcx")))
                || !File.Exists(Path.Combine(mod, $"action/script/{identity}.hks")) || layout.Models.Enemies.Single(m => m.Name == identity).SibPath?.Contains(identity, StringComparison.Ordinal) != true)
                throw new Exception($"Built person {placed.Name} differs from its source placement, its row or its own character");
            var body = FLVER2.Read(BND4.Read(Path.Combine(mod, $"chr/{identity}.chrbnd.dcx")).Files.Single(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
            // The body stands on the source human's own nodes, with one more for its meshes; every vertex is weighted to those
            // nodes, every texture named is built, and the bones state the extent of what they carry (or the host draws nothing).
            var carrying = body.Meshes.SelectMany(m => m.Vertices).SelectMany(v => Enumerable.Range(0, 4).Where(i => v.BoneWeights[i] > 0).Select(i => v.BoneIndices[i])).ToHashSet();
            if (body.Nodes.Count != humanFrame.Nodes.Count + 1 || !body.Nodes.Take(humanFrame.Nodes.Count).Select(n => n.Name).SequenceEqual(humanFrame.Nodes.Select(n => n.Name)) || body.Dummies.Count != humanFrame.Dummies.Count
                || body.Materials.Any(m => !Regex.IsMatch(m.Name, @"^aog_\d+$")) || body.Meshes.Count < 5 || body.Meshes.Any(m => !m.UseBoneWeights || m.Vertices.Count == 0 || m.NodeIndex != humanFrame.Nodes.Count)
                || body.Meshes.SelectMany(m => m.Vertices).Any(v => Enumerable.Range(0, 4).Sum(i => (int)MathF.Round(v.BoneWeights[i] * 255)) != 255)
                || body.Materials.Any(m => m.Textures.Count(t => t.Path.StartsWith("AET950_", StringComparison.Ordinal)) != 6)
                || body.Materials.SelectMany(m => m.Textures).Where(t => t.Path.StartsWith("AET950_", StringComparison.Ordinal)).Select(t => t.Path).Distinct()
                    .Any(t => !File.Exists(Path.Combine(mod, "asset/aet/aet950", Path.GetFileNameWithoutExtension(t)[..^2].ToLowerInvariant() + ".tpf.dcx")))
                || carrying.Count < 20 || carrying.Any(bone => bone >= humanFrame.Nodes.Count || !(body.Nodes[bone].BoundingBoxMin.X <= body.Nodes[bone].BoundingBoxMax.X)))
                throw new Exception($"Built person {placed.Name}'s body is not its pieces on the source human's skeleton with built textures");
            // What a person wears hides the meshes that carry certain marks (an armour row keeps a bit for each of 48, from
            // the seventh bit of its 217th byte): the built body has every mesh of its parts but those.
            var hiddenMarks = new HashSet<int>();
            foreach (int item in Enumerable.Range(0, 4).Select(slot => BitConverter.ToInt32(startRows[from.CharaInitID], 32 + 4 * slot)).Where(item => item > 0))
            {
                var armour = armourRows.TryGetValue(item, out var exact) ? exact : armourRows[item - item % 100];
                for (int mark = 0; mark < 48; mark++) if ((armour[216 + (6 + mark) / 8] >> (6 + mark) % 8 & 1) != 0) hiddenMarks.Add(mark);
            }
            int expectedMeshes = person.GetProperty("parts").EnumerateArray().Select(stem => stem.GetString()!).Sum(stem =>
            {
                var part = FLVER2.Read(BND3.Read(Path.Combine(source, $"parts/{stem}.partsbnd.dcx")).Files.OrderBy(f => f.ID).First(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                return part.Meshes.Count(m => stem.StartsWith("WP_", StringComparison.Ordinal) || Regex.Match(part.Materials[m.MaterialIndex].Name, @"^#(\d\d)#") is not { Success: true } marked
                    || !hiddenMarks.Contains(int.Parse(marked.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
            });
            if (hiddenMarks.Count == 0 || body.Meshes.Count != expectedMeshes) throw new Exception($"Built person {placed.Name} has {body.Meshes.Count} meshes where its parts show {expectedMeshes}");
            var poses = TAE.Read(BND4.Read(Path.Combine(mod, $"chr/{identity}.anibnd.dcx")).Files.Single(f => f.Name.EndsWith(".tae", StringComparison.OrdinalIgnoreCase)).Bytes);
            if (poses.ID != 200000 + int.Parse(identity[1..]) || poses.Animations.First().ID != 0) throw new Exception($"Built person {placed.Name}'s events are not its own");
        }
        // Each person runs its own talk script, with its own action, and says a line of the source's conversation text.
        var peopleSpeech = manifest.RootElement.GetProperty("talk").GetProperty("people").EnumerateArray().ToArray();
        var conversation = BND3.Read(Path.Combine(source, "msg/ENGLISH/menu.msgbnd.dcx")).Files.Where(f => f.Name.EndsWith("Conversation_.fmg", StringComparison.Ordinal)).SelectMany(f => FMG.Read(f.Bytes).Entries).Select(e => e.Text).ToHashSet();
        foreach (var said in peopleSpeech)
        {
            int saidScript = said.GetProperty("script").GetInt32(), action = said.GetProperty("action").GetInt32();
            var controller = layout.Parts.Enemies.Single(p => p.Name == said.GetProperty("name").GetString());
            var states = ESD.Read(BND4.Read(Path.Combine(mod, "script/talk/m10_01_00_00.talkesdbnd.dcx")).Files.Single(f => f.Name.EndsWith($"\\m10_01_00_00\\t{saidScript:D9}.esd", StringComparison.Ordinal)).Bytes).StateGroups[1];
            string line = hostWords["EventTextForTalk.fmg"][said.GetProperty("message").GetInt32()];
            if (controller.TalkID != saidScript || !people.Any(p => p.GetProperty("name").GetString() == controller.Name)
                || !states[2].Conditions.Single().Evaluator.AsSpan().IndexOf(BitConverter.GetBytes(action)).Equals(2) || actionRows.Rows.Count(r => r.ID == action) != 1
                || string.IsNullOrWhiteSpace(line) || !conversation.Contains(line) || action == talkAction)
                throw new Exception($"Built talk for {controller.Name} lacks its script, action or source line");
        }
        if (peopleSpeech.Length != people.Length || peopleSpeech.Select(t => t.GetProperty("message").GetInt32()).Distinct().Count() != people.Length) throw new Exception("Built people do not each have their own talk");
        if (people.Length != 2 || manifest.RootElement.GetProperty("unbuiltCharacters").GetProperty("c0000").GetInt32() != sourceEnemies.Values.Count(p => p.ModelName == "c0000") - people.Length)
            throw new Exception("Built people are not the two humans present from the start");
        // The armoured hollow's cloak rests spread out in its model and is hung; the plain hollow has no cloth.
        int HungBones(string model) => manifest.RootElement.GetProperty("wholeCharacters").GetProperty(model).GetProperty("clothBonesHung").GetInt32();
        if (HungBones("c2550") < 3 || HungBones("c2540") != 0) throw new Exception("Built enemies' cloth is not hung where the source has cloth chains that rest far from hanging");
        // Each enemy model and each person has a character to itself; the build replaces no host character.
        if (manifest.RootElement.GetProperty("wholeCharacters").EnumerateObject().Count() != enemies.Select(e => e.GetProperty("body").GetString()).Distinct().Count() || manifest.RootElement.GetProperty("wholePeople").EnumerateObject().Count() != people.Length
            || Directory.GetFiles(Path.Combine(mod, "chr"), "*.chrbnd.dcx").Select(f => Path.GetFileName(f)[..5]).Any(c => hostArchive.Contains($"chr/{c}.chrbnd.dcx")))
            throw new Exception("Built characters are not one for each enemy model in each light and each person, or a host character is replaced");
        // Navigation: one mesh for each source mesh, numbered after the built collision it lies on and listed in the map's index.
        var walk = manifest.RootElement.GetProperty("navigation");
        var meshes = BND4.Read(Path.Combine(mod, "map/m10/m10_01_00_00/m10_01_00_00.nvmhktbnd.dcx")).Files;
        var builtIndex = DCX.Decompress(File.ReadAllBytes(Path.Combine(mod, "map/m10/m10_01_00_00/m10_01_00_00.nva.dcx")));
        var sourceWalk = AgeOfGold.Navigation.SourceNavigationReader.Read(new AgeOfGold.Install(source, "DarkSoulsRemastered.exe"));
        var sourcePieces = AgeOfGold.Navigation.NavigationPieces.Split(sourceWalk.Vertices, sourceWalk.Triangles, sourceWalk.Parts).Pieces;
        var walkPieces = walk.GetProperty("pieces").EnumerateArray().ToArray();
        // The index: a 16-byte header, then sections of {identity, version, size, count} and their entries.
        var indexSections = new Dictionary<int, (int Count, byte[] Body)>();
        for (int at = 16; at < builtIndex.Length;)
        {
            int size = BitConverter.ToInt32(builtIndex, at + 8);
            indexSections.Add(BitConverter.ToInt32(builtIndex, at), (BitConverter.ToInt32(builtIndex, at + 12), builtIndex.AsSpan(at + 16, size).ToArray()));
            at += 16 + size;
        }
        if (walkPieces.Length < 20 || walkPieces.Length != sourcePieces.Length || meshes.Count != walkPieces.Length || indexSections[0].Count != walkPieces.Length)
            throw new Exception("Built navigation does not hold one mesh and one index entry for each source mesh");
        var builtVertices = new System.Numerics.Vector3[walkPieces.Length][]; var builtEdges = new byte[walkPieces.Length][][];
        var indexNames = new Dictionary<uint, int>(); int firstFace = 0;
        for (int piece = 0; piece < walkPieces.Length; piece++)
        {
            int model = walkPieces[piece].GetProperty("model").GetInt32(), faces = walkPieces[piece].GetProperty("faces").GetInt32();
            var builtMesh = new AgeOfGold.Collision.SourceTagfile(meshes[piece].Bytes, "20180100").Root.Field("namedVariants").Elements()[0].Field("variant");
            builtVertices[piece] = builtMesh.Field("vertices").Elements().Select(v => v.Floats()).Select(f => new System.Numerics.Vector3(f[0], f[1], f[2])).ToArray();
            builtEdges[piece] = builtMesh.Field("edges").Elements().Select(e => e.Raw()).ToArray();
            var entry = indexSections[0].Body.AsSpan(piece * 144, 144);
            // A mesh carries its collision's number, and that collision is placed in the built map.
            if (walkPieces[piece].GetProperty("part").GetString() != sourcePieces[piece].Part || faces != sourcePieces[piece].Triangles.Length || builtMesh.Field("faces").Elements().Length != faces
                || builtVertices[piece].Length != sourcePieces[piece].Vertices.Length || !meshes[piece].Name.EndsWith($"_{model:D6}.hkx", StringComparison.Ordinal)
                || layout.Parts.Collisions.Count(c => c.Name == $"h{model:D6}_0000") != 1
                || BitConverter.ToInt32(entry[52..]) != model || BitConverter.ToInt32(entry[56..]) != firstFace || BitConverter.ToInt32(entry[64..]) != faces || BitConverter.ToInt32(entry[76..]) != 0)
                throw new Exception($"Built navigation mesh {piece} differs from its manifest, its collision or its index entry");
            indexNames.Add(BitConverter.ToUInt32(entry[48..]), piece); firstFace += faces;
        }
        if (firstFace != walk.GetProperty("faces").GetInt32() || firstFace < 1000) throw new Exception("Built navigation faces do not add up");
        // Joins, read as the host reads them: each names two meshes and a run of edge pairs; the two edges of a pair are
        // border edges of their meshes and run between the same two points in opposite directions.
        int joinedEdges = 0;
        for (int join = 0; join < indexSections[4].Count; join++)
        {
            var record = indexSections[4].Body.AsSpan(join * 32, 32);
            int from = indexNames[BitConverter.ToUInt32(record)], to = indexNames[BitConverter.ToUInt32(record[4..])], pairs = BitConverter.ToInt32(record[8..]), firstPair = BitConverter.ToInt32(record[16..]);
            if (from == to || pairs < 1) throw new Exception("A built navigation join is empty or joins a mesh to itself");
            for (int pair = firstPair; pair < firstPair + pairs; pair++)
            {
                var edges = indexSections[5].Body.AsSpan(pair * 16, 16);
                int edge = BitConverter.ToInt32(edges[4..]), otherEdge = BitConverter.ToInt32(edges[12..]);
                byte[] here = builtEdges[from][edge], there = builtEdges[to][otherEdge];
                if (BitConverter.ToInt32(edges) != edge / 3 || BitConverter.ToInt32(edges[8..]) != otherEdge / 3 || BitConverter.ToInt32(here, 8) != -1 || BitConverter.ToInt32(there, 8) != -1
                    || builtVertices[from][BitConverter.ToInt32(here, 0)] != builtVertices[to][BitConverter.ToInt32(there, 4)] || builtVertices[from][BitConverter.ToInt32(here, 4)] != builtVertices[to][BitConverter.ToInt32(there, 0)])
                    throw new Exception("A built navigation join pairs edges that do not coincide on their meshes' borders");
                joinedEdges++;
            }
        }
        // Gates: each names a neighbour and its own place among that neighbour's gates, where the same position is found.
        int gates = indexSections[8].Count;
        var gateRanges = Enumerable.Range(0, walkPieces.Length).Select(p => (First: (int)BitConverter.ToInt16(indexSections[0].Body, p * 144 + 72), Count: (int)BitConverter.ToInt16(indexSections[0].Body, p * 144 + 74))).ToArray();
        for (int piece = 0; piece < walkPieces.Length; piece++)
            for (int gate = gateRanges[piece].First; gate < gateRanges[piece].First + gateRanges[piece].Count; gate++)
            {
                var node = indexSections[8].Body.AsSpan(gate * 48, 48);
                int other = BitConverter.ToInt16(node[12..]), counterpart = gateRanges[other].First + BitConverter.ToInt16(node[14..]);
                var twin = indexSections[8].Body.AsSpan(counterpart * 48, 48);
                if (other == piece || BitConverter.ToInt16(node[14..]) >= gateRanges[other].Count || !node[..12].SequenceEqual(twin[..12]) || BitConverter.ToInt16(twin[12..]) != piece
                    || gateRanges[piece].First + BitConverter.ToInt16(twin[14..]) != gate || BitConverter.ToUInt16(node[(16 + 2 * (gate - gateRanges[piece].First))..]) != 0)
                    throw new Exception("A built navigation gate does not match its counterpart");
            }
        if (indexSections[4].Count != walk.GetProperty("joins").GetInt32() || indexSections[4].Count < walkPieces.Length || joinedEdges != indexSections[5].Count || gates != gateRanges.Sum(r => r.Count) || gates < walkPieces.Length)
            throw new Exception("Built navigation joins or gates differ from the manifest");
        // The border is widened toward the walls: the source positions the manifest counts as moved differ in the built
        // meshes, by no more than a corner's share of the reach; every other vertex keeps its place.
        var border = walk.GetProperty("border");
        float reach = border.GetProperty("reach").GetSingle(), furthest = border.GetProperty("furthest").GetSingle();
        var displacements = Enumerable.Range(0, walkPieces.Length).SelectMany(p => builtVertices[p].Select((v, i) => (Source: sourcePieces[p].Vertices[i], Shift: v - sourcePieces[p].Vertices[i]))).ToArray();
        // An inner vertex keeps its place, so the displacement most vertices share is the map's offset.
        var shift = displacements.GroupBy(d => ((int)MathF.Round(d.Shift.X * 100), (int)MathF.Round(d.Shift.Y * 100), (int)MathF.Round(d.Shift.Z * 100))).MaxBy(g => g.Count())!.First().Shift;
        var movedFrom = displacements.Where(d => new System.Numerics.Vector2(d.Shift.X - shift.X, d.Shift.Z - shift.Z).Length() > 1e-3f).ToArray();
        // A vertex shared by two pieces is counted once; the manifest also counts the few moves shorter than this reading resolves.
        int moved = movedFrom.Select(d => d.Source).Distinct().Count();
        if (displacements.Length - movedFrom.Length < displacements.Length / 8) throw new Exception("Built navigation vertices do not follow the source's");
        if (reach != 0.5f || Math.Abs(moved - border.GetProperty("moved").GetInt32()) > moved / 100 || moved < 300 || !(furthest > reach / 2) || furthest > 2 * reach + 1e-3f
            || movedFrom.Any(d => new System.Numerics.Vector2(d.Shift.X - shift.X, d.Shift.Z - shift.Z).Length() > 2 * reach + 1e-2f))
            throw new Exception($"Built navigation border is not widened as its manifest says: {moved} moved, furthest {furthest}");
        var materials = BND4.Read(Path.Combine(mod, "material/allmaterial.matbinbnd.dcx")).Files
            .ToLookup(f => string.Join('/', f.Name.Replace('\\', '/').Split('/')[^3..]).ToLowerInvariant(), f => f.Bytes);
        var shaders = File.ReadAllBytes(Path.Combine(mod, "shader/shaderbdle.shaderbdlebnd.dcx"));
        int surfaces = 0, cutouts = 0, blends = 0, scrolling = 0; var families = new HashSet<string>(StringComparer.Ordinal);
        // The source placements that cast shadows, read here from the source map itself: none of its map pieces, most
        // of its objects, its characters.
        var sourceParts = MSB1.Read(Path.Combine(source, "map/MapStudio/m10_02_00_00.msb")).Parts;
        var casters = sourceParts.MapPieces.Cast<MSB1.Part>().Concat(sourceParts.Objects).Concat(sourceParts.Enemies).Where(p => p.IsShadowSrc != 0).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var builtFrom = manifest.RootElement.GetProperty("scenery").GetProperty("placements").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        int shadeless = 0, casting = 0;
        foreach (var model in pieces.Select(p => p.ModelName).Distinct())
        {
            bool casts = pieces.Where(p => p.ModelName == model).Any(p => casters.Contains(builtFrom[p.Name]));
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
                // The host draws scenery into its shadow maps unless a definition forbids it: a model none of whose source
                // placements casts is drawn by definitions that do, named apart from the family's casting one.
                bool forbidden = (bool)definition.Params.Single(p => p.Name == "g_GX_bDisableShadowCasting").Value;
                string named = Path.GetFileNameWithoutExtension(mtd[^1]);
                if (named != (casts ? family : family + "_shadeless") || !casts && !forbidden) throw new Exception($"Built scenery model {model} casts shadows where its source does not, or the reverse: {named}");
                if (casts) casting++; else shadeless++;
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
        // The map's 330 surfaces less the 5 unbuilt, the bonfire's 5 and the Fire Keeper's, plus the statue and the urns: six
        // object models (two of the urns once more for a second lighting row), each of one opaque and one cutout surface.
        if (shadeless == 0 || casting == 0) throw new Exception("Built scenery does not hold both casting and shadeless surfaces");
        if (surfaces != 360 || cutouts != 84 || blends != 34) throw new Exception($"Built opaque and cutout surface inventory differs: {surfaces} surfaces, {cutouts} cutouts, {blends} blended");
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
        // A whole character is laid out for the host's plain character material and drawn by the build's own family: it
        // wears the source's own colour, specular and relief maps, unchanged, with the two cubes that light it and the
        // source's reflectance table; its definition carries one source definition's two colours.
        DDS Built(string texture)
        {
            var package = File.ReadAllBytes(Path.Combine(mod, "asset/aet/aet950", Path.GetFileNameWithoutExtension(texture)[..^2].ToLowerInvariant() + ".tpf.dcx"));
            return new DDS(AgeOfGold.Scene.TextureReader.ReadPackage(package).Single().Bytes);
        }
        var builtTextures = manifest.RootElement.GetProperty("textures").EnumerateObject().ToDictionary(t => t.Value.GetString()!, t => t.Name, StringComparer.OrdinalIgnoreCase);
        var characterLight = manifest.RootElement.GetProperty("characterLight");
        int cutoutMeshes = 0, dressedSurfaces = 0;
        // The light a character takes from a source collision, read here from the source map itself: the collision's
        // light, fog and scattering rows, and the number its light capture's name ends in.
        var sourceMap = MSB1.Read(Path.Combine(source, "map/MapStudio/m10_02_00_00.msb"));
        (int Light, int Fog, int Scattering, int Capture) LightOn(string collision)
        {
            var part = sourceMap.Parts.Collisions.Single(c => c.Name == collision);
            return (part.LightID, part.FogID, part.ScatterID, int.Parse(Regex.Match(sourceMap.Events.Environments[part.EnvLightMapSpotIndex].Name, @"\d+$").Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        int lights = 0;
        // The source's definitions: the patch binder's over the base one's, and the first of a repeated name.
        var sourceMaterials = new Dictionary<string, MTD>(StringComparer.OrdinalIgnoreCase);
        foreach (string binder in new[] { "mtd/Mtd.mtdbnd.dcx", "mtd/MtdPatch.mtdbnd.dcx" })
            foreach (var group in BND3.Read(Path.Combine(source, binder)).Files.GroupBy(f => Path.GetFileNameWithoutExtension(f.Name.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase))
                sourceMaterials[group.Key] = MTD.Read(group.First().Bytes);
        // A definition's colour times its power.
        float[] Factor(string definition, string colour) => ((float[])sourceMaterials[definition].Params.Single(q => q.Name == colour).Value)
            .Select(v => v * (float)sourceMaterials[definition].Params.Single(q => q.Name == colour + "Power").Value).ToArray();
        bool Close(float[] built, float[] stated) => built.Take(3).Zip(stated, (x, y) => MathF.Abs(x - y) <= 0.0006f).All(same => same);
        foreach (var (identity, body) in bodyFiles)
            foreach (var material in body.Materials)
            {
                // Every placement of a body stands in one light, and the body's definitions and cubes are that light's.
                var on = stoodOn[identity].Select(LightOn).Distinct().ToArray();
                if (on.Length != 1) throw new Exception($"Built body {identity} is placed in more than one light");
                string family = $"aog_character_{on[0].Light}_{on[0].Fog}_{on[0].Scattering}";
                var named = Regex.Match(material.MTD, @"\\(" + family + @"_\d{2})\.matxml$");
                if (!named.Success) throw new Exception($"Built body {identity} is drawn by an unexpected definition: {material.MTD}");
                var definition = MATBIN.Read(materials["chr_base/matxml/" + named.Groups[1].Value + ".matbin"].Single());
                bool cut = ((int[])definition.Params.Single(q => q.Name == "g_AlphaRef").Value).SequenceEqual([128, 0]);
                if (!cut && !((int[])definition.Params.Single(q => q.Name == "g_AlphaRef").Value).SequenceEqual([0, 0])) throw new Exception("A character definition has an unexpected coverage threshold");
                cutoutMeshes += cut ? 1 : 0;
                if (!definition.ShaderPath.EndsWith(@"\" + family + ".spx", StringComparison.Ordinal) || !definition.SourcePath.EndsWith(@"\" + family + ".matxml", StringComparison.Ordinal)
                    || !definition.Samplers.Select(q => q.Type).SequenceEqual(material.Textures.Select(t => t.ParamName)) || material.Textures.Count != 6)
                    throw new Exception($"Built body {identity}'s definition is not the build's family's, or names other inputs than its mesh fills");
                string Wears(string input) => builtTextures[material.Textures.Single(t => t.ParamName.EndsWith(input, StringComparison.Ordinal)).Path];
                string Named(string input) => material.Textures.Single(t => t.ParamName.EndsWith(input, StringComparison.Ordinal)).Path;
                // The colour map is read as the source reads it, without the display curve; the specular map sits where the host keeps metal.
                if (!Wears("_AlbedoMap_0").StartsWith("aog:surface:", StringComparison.Ordinal) || !(Wears("_MetallicMap_0").StartsWith("aog:surface:", StringComparison.Ordinal) || Wears("_MetallicMap_0") == "aog:dull")
                    || !(Wears("_NormalMap_4").StartsWith("aog:surface:", StringComparison.Ordinal) || Wears("_NormalMap_4") == "aog:flat-normal")
                    || Built(Named("_AlbedoMap_0")).GetDXGIFormat() is not (DDS.DXGI_FORMAT.BC1_UNORM or DDS.DXGI_FORMAT.BC7_UNORM) || Built(Named("_NormalMap_4")).GetDXGIFormat() != DDS.DXGI_FORMAT.BC5_UNORM)
                    throw new Exception($"Built body {identity} does not wear the source's own maps: {Wears("_AlbedoMap_0")}, {Wears("_MetallicMap_0")}, {Wears("_NormalMap_4")}");
                var added = material.Textures.Skip(3).Select(t => builtTextures[t.Path]).ToArray();
                // The cubes are those captured for the collision; the sky cube is one of the capture's four.
                if (added.Length != 3 || added[0] != $"GI_EnvDif_m10_02_{on[0].Capture:D4}" || !Regex.IsMatch(added[1], $"^GI_EnvSpc_m10_02_{on[0].Capture:D4}_[0-3]$") || added[2] != "aog:table"
                    || Built(material.Textures[5].Path).GetDXGIFormat() != DDS.DXGI_FORMAT.BC5_UNORM || Built(material.Textures[5].Path).dwWidth != 256 || Built(material.Textures[4].Path).GetDXGIFormat() != DDS.DXGI_FORMAT.BC6H_UF16)
                    throw new Exception($"Built body {identity} lacks the diffuse cube, the sky cube or the reflectance table: {string.Join(", ", added)}");
                dressedSurfaces++;
                // An enemy's two colours are those of one of its source definitions.
                float[] plainColour = (float[])definition.Params.Single(q => q.Name == "g_DiffuseMapColor").Value, metalColour = (float[])definition.Params.Single(q => q.Name.EndsWith("_snp_0_color_0", StringComparison.Ordinal)).Value;
                // And the share of a shadow it takes is that definition's.
                float share = (float)definition.Params.Single(q => q.Name.EndsWith("_snp_0_float_1", StringComparison.Ordinal)).Value;
                if (sourceDefinitions.TryGetValue(identity, out var from) && !from.Any(d => Close(plainColour, Factor(d, "g_DiffuseMapColor")) && Close(metalColour, Factor(d, "g_SpecularMapColor"))
                    && share == (float)sourceMaterials[d].Params.Single(q => q.Name == "g_ShadowPowMul").Value))
                    throw new Exception($"Built body {identity} wears colours or a share of shadow no source definition of its gives: {string.Join(",", plainColour)} and {string.Join(",", metalColour)}, {share}");
            }
        if (cutoutMeshes == 0 || dressedSurfaces == 0 || sourceDefinitions.Count == 0) throw new Exception($"Built characters lack torn cloth's cut-out definition or dressed surfaces: {cutoutMeshes} cut-out meshes, {dressedSurfaces} surfaces");
        // The family: the host's plain character family with three inputs more, vertex stages that forward the vector to the
        // camera, and deferred pixel stages that mark their light as a character's.
        var sourceInstall = new Install(source, "DarkSoulsRemastered.exe");
        var program = characterLight.GetProperty("program");
        var lightRows = AgeOfGold.Params.SourceLightingReader.Read(sourceInstall).Sets.ToDictionary(r => r.Id);
        float hemisphereStrength = SourceRendererSettings.Read(sourceInstall).HemisphereStrength;
        // A row's colour times its intensity, raised to linear light.
        float[] Raised(AgeOfGold.Params.SourceLightingReader.Colour colour, float strength) => new[] { colour.Red, colour.Green, colour.Blue }.Select(v => MathF.Pow(v * colour.Intensity, 2.2f) * strength).ToArray();
        bool Same(JsonElement stated, float[] expected) => stated.EnumerateArray().Select(v => v.GetSingle()).Zip(expected, (x, y) => MathF.Abs(x - y) <= 1e-5f * MathF.Max(1, y)).All(same => same) && stated.GetArrayLength() == 3;
        foreach (var light in program.GetProperty("lights").EnumerateArray())
        {
            string family = light.GetProperty("family").GetString()!;
            var row = lightRows[light.GetProperty("row").GetInt32()];
            // A family is named for its rows; its two cubes are scaled by the row's environment colours and its hemisphere
            // is the row's, at the source's own ambient strength.
            if (family != $"aog_character_{row.Id}_{light.GetProperty("fog").GetInt32()}_{light.GetProperty("scattering").GetInt32()}"
                || !stoodOn.Values.SelectMany(on => on.Select(LightOn)).Any(on => (on.Light, on.Fog, on.Scattering) == (row.Id, light.GetProperty("fog").GetInt32(), light.GetProperty("scattering").GetInt32()))
                || !Same(light.GetProperty("diffuse"), Raised(row.DiffuseEnvironment.Colour, 1)) || !Same(light.GetProperty("sky"), Raised(row.SpecularEnvironment.Colour, 1))
                || !Same(light.GetProperty("upper"), Raised(row.Upper, hemisphereStrength)) || !Same(light.GetProperty("lower"), Raised(row.Lower, hemisphereStrength)) || !light.GetProperty("air").GetBoolean())
                throw new Exception("A character family's light is not its rows': " + family);
            lights++;
            using var characterFamily = new BND4Reader(ShaderArchive.ReadFamily(shaders, family + ".shaderbdle"));
            byte[] Member(string suffix) => characterFamily.ReadFile(characterFamily.Files.Single(f => f.Name.EndsWith(family + suffix, StringComparison.Ordinal)));
            if (ShaderMetadata.Read(Member(".metaparam")).Textures.Count != 6) throw new Exception("Built character family does not declare six texture inputs");
            foreach (string stage in new[] { "_Gbuf.vpo", "_Gbuf_Skin.vpo", "_Gbuf@[cl].vpo", "_Gbuf_Skin@[cl].vpo" })
                if (new ShaderSignatures(compiler.Disassemble(Member(stage))).Members(true).Count(m => m.Name == "TANGENT" && m.Index == 8) != 1) throw new Exception("Built character vertex stage does not forward the view: " + stage);
            foreach (string stage in new[] { "_Gbuf.ppo", "_Gbuf_[A].ppo" })
            {
                string pixel = compiler.Disassemble(Member(stage));
                if (!pixel.Contains("%aog_skytexel = call %dx.types.ResRet.f32 @dx.op.sampleLevel.f32(", StringComparison.Ordinal) || !pixel.Contains("i8 3, float -2.000000e+00)", StringComparison.Ordinal)
                    || !pixel.Contains("%aog_airlitdarkr = ", StringComparison.Ordinal) || !pixel.Contains("%aog_turned = ", StringComparison.Ordinal)
                    || !Regex.IsMatch(pixel, @"storeOutput\.f32\(i32 5, i32 1, i32 0, i8 0, float 1\.000000e\+00\)") || !Regex.IsMatch(pixel, @"storeOutput\.f32\(i32 5, i32 1, i32 0, i8 1, float 0\.000000e\+00\)"))
                    throw new Exception("Built character pixel stage does not light and mark its surface: " + stage);
            }
        }
        // The area's characters stand in more than one light, and one model in two.
        if (lights < 2 || lights != stoodOn.Values.SelectMany(on => on.Select(LightOn)).Select(on => (on.Light, on.Fog, on.Scattering)).Distinct().Count()
            || !manifest.RootElement.GetProperty("wholeCharacters").EnumerateObject().Any(c => c.Name.Contains('+')))
            throw new Exception("Built character families are not one for each set of rows a built character stands in");
        // The source's own settings: a shadow's power of two, and the cosine and edge of a surface's own shadow.
        if (program.GetProperty("shadePower").GetSingle() != 2 || program.GetProperty("shadeTurn").GetSingle() != 0.2f || program.GetProperty("shadeEdge").GetSingle() != 5 || program.GetProperty("probeGain").GetSingle() != 1
            || !(program.GetProperty("glow").GetSingle() > 0))
            throw new Exception("The character program's settings are not the source's");
        // Characters take the source's light. One host sun stays on, without highlights, from where the source's
        // shadows are cast, to shade what a character stores facing it; the environment boxes keep the host's own
        // multipliers; and every baked volume, for each state of the sky and each quality, holds throughout the arrival
        // row's hemisphere light.
        var sunColour = program.GetProperty("sunColour").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        float sunLight = sunColour.Max();
        var shadowRow = AgeOfGold.Params.SourceAtmosphereReader.Shadows(sourceInstall)[characterLight.GetProperty("shadow").GetInt32()];
        // The light shines along (cos x sin y, -sin x, cos x cos y) for its two angles; a surface looks back along it.
        double shadowX = shadowRow.Elevation * Math.PI / 180, shadowY = shadowRow.Azimuth * Math.PI / 180;
        var towardsLight = new System.Numerics.Vector3((float)(-Math.Cos(shadowX) * Math.Sin(shadowY)), (float)Math.Sin(shadowX), (float)(-Math.Cos(shadowX) * Math.Cos(shadowY)));
        var statedSun = program.GetProperty("sun").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        if (characterLight.GetProperty("shadow").GetInt32() != manifest.RootElement.GetProperty("arrival").GetProperty("selection").GetProperty("Shadow").GetInt32()
            || System.Numerics.Vector3.Distance(new(statedSun[0], statedSun[1], statedSun[2]), towardsLight) > 1e-5f || !(sunLight > 0)
            || program.GetProperty("shadeStart").GetSingle() != shadowRow.FadeStart || program.GetProperty("shadeLength").GetSingle() != shadowRow.FadeLength || program.GetProperty("shade").GetSingle() != shadowRow.Density)
            throw new Exception("The shade the manifest states is not the arrival's shadow row's");
        if (new[] { "Light SpecColor0", "Light DiffColor1", "Light SpecColor1", "Light LocalLight DiffColor1", "Light LocalLight SpecColor1", "Light LocalLight DiffColor2", "Light LocalLight SpecColor2" }
            .Any(light => graphics["Directional " + light].Single().Values.Any(v => ((System.Numerics.Vector4)v).W != 0))
            // Measured in the host: a sun of strength one adds 0.40 of a stored colour that faces it.
            // The stand-in sun lights a facing surface of the host's by less than its own colour.
            || sunColour.Length != 3 || sunColour.Any(v => !(v > 0)) || !(sunLight < 1)
            || graphics["Directional Light DiffColor0"].Single().Values.Any(v => System.Numerics.Vector4.Distance((System.Numerics.Vector4)v, new(sunColour[0] / sunLight, sunColour[1] / sunLight, sunColour[2] / sunLight, sunLight / 0.40f)) > 1e-5f)
            || graphics["Directional Light Angle0"].Single().Values.Any(v =>
            {
                var angle = (System.Numerics.Vector2)v;
                return System.Numerics.Vector3.Distance(new(MathF.Cos(angle.X) * MathF.Sin(angle.Y), -MathF.Sin(angle.X), MathF.Cos(angle.X) * MathF.Cos(angle.Y)), towardsLight) > 1e-5f;
            }))
            throw new Exception("Built graphics parameters do not keep exactly one sun, from where the source's shadows are cast");
        var hostBoxes = MSBE.Read(hostArchive.Read("map/mapstudio/m10_01_00_00.msb.dcx")).Regions.EnvironmentMapEffectBoxes.ToDictionary(b => b.Name, b => b.PointLightMult);
        if (hostBoxes.Count == 0 || layout.Regions.EnvironmentMapEffectBoxes.Count != hostBoxes.Count || layout.Regions.EnvironmentMapEffectBoxes.Any(b => b.PointLightMult != hostBoxes[b.Name]))
            throw new Exception("Built environment boxes do not keep the host's multipliers");
        float[] Term(string name) => characterLight.GetProperty(name).EnumerateArray().Select(v => v.GetSingle()).ToArray();
        float[][] lightTerms = [Term("constant"), Term("x"), Term("y"), Term("z")];
        int lightRowId = characterLight.GetProperty("row").GetInt32();
        var lightRow = lightRows[lightRowId];
        float Linear(float colour, float intensity) => MathF.Pow(colour * intensity, 2.2f) * hemisphereStrength;
        float[] above = [Linear(lightRow.Upper.Red, lightRow.Upper.Intensity), Linear(lightRow.Upper.Green, lightRow.Upper.Intensity), Linear(lightRow.Upper.Blue, lightRow.Upper.Intensity)];
        float[] below = [Linear(lightRow.Lower.Red, lightRow.Lower.Intensity), Linear(lightRow.Lower.Green, lightRow.Lower.Intensity), Linear(lightRow.Lower.Blue, lightRow.Lower.Intensity)];
        if (lightRowId != manifest.RootElement.GetProperty("arrival").GetProperty("selection").GetProperty("Light").GetInt32() || lightTerms.Any(t => t.Length != 3 || !t.All(float.IsFinite))
            || Enumerable.Range(0, 3).Any(c => MathF.Abs(lightTerms[0][c] - (above[c] + below[c]) / 2) > 1e-5f || MathF.Abs(lightTerms[2][c] - (above[c] - below[c]) / 2) > 1e-5f || lightTerms[1][c] != 0 || lightTerms[3][c] != 0))
            throw new Exception("The baked light the manifest states is not the arrival row's hemisphere colour");
        var volumePaths = characterLight.GetProperty("volumes").EnumerateArray().Select(v => v.GetString()!).ToArray();
        // Seven states of the sky in three qualities.
        if (volumePaths.Length != 21 || volumePaths.Distinct().Count() != 21) throw new Exception($"Built baked light covers {volumePaths.Length} state and quality binders, not 21");
        var sky = AgeOfGold.Scene.TextureReader.ReadFile(sourceInstall, "map/m10/GI_EnvM_m10", $"GI_Env_m10_02_{characterLight.GetProperty("capture").GetInt32():D4}")
            .Single(t => t.Name == characterLight.GetProperty("sky").GetString()).Bytes;
        var skyHeader = new DDS(sky);
        static int Levels(int size, int first, int count) => Enumerable.Range(first, count).Sum(level => { int blocks = (Math.Max(1, size >> level) + 3) / 4; return blocks * blocks * 16; });
        int skyFace = Levels(skyHeader.dwWidth, 0, skyHeader.dwMipMapCount), cubes = 0;
        // Every environment box reflects at the one strength the manifest states.
        float reflection = characterLight.GetProperty("reflection").GetSingle();
        if (!(reflection > 0) || layout.Regions.EnvironmentMapEffectBoxes.Any(q => q.SpecularLightMult != reflection))
            throw new Exception("Built environment boxes do not reflect the sky at the stated strength");
        // The composition stage shows the scenery's marked light alone, and a character's with the light the host shaded;
        // the sun's stages hand a marked surface the shadow alone, and the colour stage multiplies its stored light back in.
        var renderingStages = File.ReadAllBytes(Path.Combine(mod, "shader/GXRendererShader.shaderbnd.dcx"));
        string composition = compiler.Disassemble(ShaderArchive.Read(renderingStages, "GXLightComposite.ppo"));
        if (!composition.Contains("%aog_marked = fcmp fast olt float", StringComparison.Ordinal) || !composition.Contains("%aog_shaded = fcmp fast olt float", StringComparison.Ordinal))
            throw new Exception("Built composition stage does not keep the build's own light apart");
        foreach (string stage in new[] { "GXLightAcc_DDirLight1.ppo", "GXLightAcc_DDirLightShadowed1.ppo", "GXLightAcc_DDirLightShadowed1_Cloud.ppo" })
        {
            string sunStage = compiler.Disassemble(ShaderArchive.Read(renderingStages, stage));
            // The stages for a sun that casts shadows also hand it the cast shadow.
            if (!sunStage.Contains("%aog_shadow = select i1 %aog_on, float %aog_scaled, float 0.000000e+00", StringComparison.Ordinal)
                || sunStage.Contains("%aog_cast = fmul fast float", StringComparison.Ordinal) != stage.Contains("Shadowed", StringComparison.Ordinal))
                throw new Exception("Built sun stage does not hand a marked surface its shadow: " + stage);
        }
        if (!compiler.Disassemble(ShaderArchive.Read(renderingStages, "GXLightCmbAlbedo.ppo")).Contains("%aog_out00 = select i1 %aog_marked, float %aog_light0,", StringComparison.Ordinal))
            throw new Exception("Built colour stage does not apply a marked surface's stored light");
        foreach (string path in volumePaths)
        {
            var built = BND4.Read(Path.Combine(mod, path)); var installed = BND4.Read(hostArchive.Read(path));
            if (!built.Files.Select(f => (f.ID, f.Name)).SequenceEqual(installed.Files.Select(f => (f.ID, f.Name)))) throw new Exception("Built baked light does not keep the host's members: " + path);
            int volumes = 0;
            for (int i = 0; i < built.Files.Count; i++)
            {
                var term = Regex.Match(Path.GetFileNameWithoutExtension(built.Files[i].Name.Replace('\\', '/')), @"_GIIV\d{4}_\d{2}_([WXYZ])$");
                if (!term.Success)
                {
                    // A reflection cube keeps the host's package and header and holds, face by face, the sky's levels of its own size.
                    var cube = AgeOfGold.Scene.TextureReader.ReadPackage(built.Files[i].Bytes).Single().Bytes;
                    var to = new DDS(cube);
                    int skip = System.Numerics.BitOperations.Log2((uint)(skyHeader.dwWidth / to.dwWidth)), face = Levels(to.dwWidth, 0, to.dwMipMapCount);
                    if (built.Files[i].Bytes.Length != installed.Files[i].Bytes.Length || cube.Length != to.DataOffset + 6 * face || to.dwWidth << skip != skyHeader.dwWidth
                        || Enumerable.Range(0, 6).Any(f => !cube.AsSpan(to.DataOffset + f * face, face).SequenceEqual(sky.AsSpan(skyHeader.DataOffset + f * skyFace + Levels(skyHeader.dwWidth, 0, skip), face))))
                        throw new Exception("A built reflection cube does not hold the source's sky: " + built.Files[i].Name);
                    cubes++;
                    continue;
                }
                var dds = AgeOfGold.Scene.TextureReader.ReadPackage(built.Files[i].Bytes).Single().Bytes;
                var header = new DDS(dds);
                var expected = AgeOfGold.Maps.HostIrradiance.Encode(lightTerms["WXYZ".IndexOf(term.Groups[1].Value, StringComparison.Ordinal)]);
                var texels = BlockTextureCheck.ReadOneLine(dds.AsSpan(header.DataOffset, 16));
                bool same = dds.Length == AgeOfGold.Scene.TextureReader.ReadPackage(installed.Files[i].Bytes).Single().Bytes.Length && (dds.Length - header.DataOffset) % 16 == 0;
                for (int at = header.DataOffset + 16; same && at < dds.Length; at += 16) same = dds.AsSpan(at, 16).SequenceEqual(dds.AsSpan(header.DataOffset, 16));
                if (!same || Enumerable.Range(0, 16).Any(t => Math.Abs(texels[t * 4] - expected[0]) > 1 || Math.Abs(texels[t * 4 + 1] - expected[1]) > 1 || Math.Abs(texels[t * 4 + 2] - expected[2]) > 1 || texels[t * 4 + 3] != expected[3]))
                    throw new Exception("A built volume does not hold the stated light throughout: " + built.Files[i].Name);
                volumes++;
            }
            if (volumes == 0 || volumes % 4 != 0) throw new Exception("Built baked light has no whole sets of volumes: " + path);
        }
        if (cubes == 0) throw new Exception("Built baked light has no reflection cubes");
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
        Console.WriteLine($"PASS: Firelink build repeats across {files.Length} files; {surfaces} opaque lit surfaces, {pieces.Length} scenery placements and 38 terrain placements read back; installs unchanged");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
