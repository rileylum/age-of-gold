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
        // Living enemies: each source enemy the manifest lists stands in the built map as the host character that carries it,
        // on a built collision, under a build-owned row with the source's body size and the display mark of its own body.
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
        var bodyFiles = new Dictionary<string, FLVER2>(StringComparer.Ordinal); var marksOf = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
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
                || BitConverter.ToSingle(raw, 340) != size.Height || BitConverter.ToSingle(raw, 344) != size.Radius || System.Numerics.BitOperations.PopCount((uint)shown) != 1)
                throw new Exception($"Built enemy {placed.Name} differs from its source placement, its collision or its row");
            if (!bodyFiles.TryGetValue(placed.ModelName, out var body))
            {
                bodyFiles[placed.ModelName] = body = FLVER2.Read(BND4.Read(Path.Combine(mod, $"chr/{placed.ModelName}.chrbnd.dcx")).Files.Single(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                var hostBody = FLVER2.Read(BND4.Read(hostArchive.Read($"chr/{placed.ModelName}.chrbnd.dcx")).Files.Single(f => f.Name.EndsWith(".flver", StringComparison.OrdinalIgnoreCase)).Bytes);
                // The host's skeleton is kept whole; no host mesh remains; every vertex is weighted to host bones and every texture named is built.
                if (body.Nodes.Count != hostBody.Nodes.Count || !body.Nodes.Select(n => n.Name).SequenceEqual(hostBody.Nodes.Select(n => n.Name)) || body.Dummies.Count != hostBody.Dummies.Count
                    || body.Materials.Any(m => !Regex.IsMatch(m.Name, @"^#\d\d#aog_\d+$")) || body.Meshes.Any(m => !m.UseBoneWeights || m.Vertices.Count == 0)
                    || body.Meshes.SelectMany(m => m.Vertices).Any(v => Enumerable.Range(0, 4).Sum(i => (int)MathF.Round(v.BoneWeights[i] * 255)) != 255)
                    || body.Materials.Any(m => m.Textures.Count(t => t.Path.StartsWith("AET950_", StringComparison.Ordinal)) != 3)
                    || body.Materials.SelectMany(m => m.Textures).Where(t => t.Path.StartsWith("AET950_", StringComparison.Ordinal)).Select(t => t.Path).Distinct()
                        .Any(t => !File.Exists(Path.Combine(mod, "asset/aet/aet950", Path.GetFileNameWithoutExtension(t)[..^2].ToLowerInvariant() + ".tpf.dcx"))))
                    throw new Exception("Built enemy body is not source meshes on the host's skeleton with built textures");
                marksOf[placed.ModelName] = [];
            }
            // The row shows a mark that some mesh of the body carries, and placements of one source model share it.
            int mark = System.Numerics.BitOperations.TrailingZeroCount(shown);
            if (!body.Materials.Any(m => m.Name.StartsWith($"#{mark:D2}#", StringComparison.Ordinal))) throw new Exception($"Built enemy {placed.Name} shows a display mark no mesh carries");
            marksOf[placed.ModelName].Add(mark);
        }
        // People: each source human the manifest lists stands in the built map on a built collision, under its own row, which
        // shows a display mark of its own that meshes of the body carry and has the standing of an installed friendly character.
        var people = manifest.RootElement.GetProperty("people").EnumerateArray().ToArray();
        var peopleMarks = new HashSet<int>();
        foreach (var person in people)
        {
            var placed = layout.Parts.Enemies.Single(p => p.Name == person.GetProperty("name").GetString());
            var from = sourceEnemies[person.GetProperty("source").GetString()!];
            var raw = (byte[])builtCharacters.Rows.Single(r => r.ID == placed.NPCParamID)["raw"].Value;
            int shown = BitConverter.ToUInt16(raw, 330), mark = System.Numerics.BitOperations.TrailingZeroCount(shown);
            if (from.ModelName != "c0000" || placed.Name != "aog_" + from.Name || placed.EntityID != 0 || layout.Parts.Collisions.Count(c => c.Name == placed.CollisionPartName) != 1
                || (placed.Position - enemyOffset - from.Position).Length() > 1e-3f || placed.Rotation != from.Rotation || System.Numerics.BitOperations.PopCount((uint)shown) != 1
                || !peopleMarks.Add(mark) || marksOf[placed.ModelName].Contains(mark) || !bodyFiles[placed.ModelName].Materials.Any(m => m.Name.StartsWith($"#{mark:D2}#", StringComparison.Ordinal))
                // Enemies' rows carry side 6; a person's row does not.
                || raw[307] == 6 || person.GetProperty("parts").GetArrayLength() < 5)
                throw new Exception($"Built person {placed.Name} differs from its source placement, its row or its body");
        }
        if (people.Length != 2 || manifest.RootElement.GetProperty("unbuiltCharacters").GetProperty("c0000").GetInt32() != sourceEnemies.Values.Count(p => p.ModelName == "c0000") - people.Length)
            throw new Exception("Built people are not the two humans present from the start");
        if (marksOf.Values.Sum(m => m.Count) != enemyModels.Length || bodyFiles.Sum(b => b.Value.Materials.Select(m => m.Name[..4]).Distinct().Count()) != enemyModels.Length + people.Length)
            throw new Exception("Built bodies and display marks are not one for each source model and person");
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
        Console.WriteLine($"PASS: Firelink build repeats across {files.Length} files; {surfaces} opaque lit surfaces, {pieces.Length} scenery placements and 38 terrain placements read back; installs unchanged");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new ArgumentException("Set " + name);
}
