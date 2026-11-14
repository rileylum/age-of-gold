using System.Numerics;
using AgeOfGold.Maps;
using SoulsFormats;

internal static class MapSceneryCheck
{
    internal static void Run()
    {
        var layout = new MSBE();
        layout.Models.Players.Add(new() { Name = "c0000" });
        layout.Parts.Players.Add(new() { Name = "c0000_0000", ModelName = "c0000" });
        layout.Models.MapPieces.Add(new() { Name = "m000000" });
        layout.Parts.MapPieces.Add(new() { Name = "m000000_0000", ModelName = "m000000", Position = new(1, 2, 3), EntityID = 77 });
        layout.Models.Enemies.Add(new() { Name = "c1000" });
        layout.Parts.Enemies.Add(new() { Name = "c1000_0000", ModelName = "c1000" });
        layout.Regions.SFX.Add(new() { Name = "authored-effect", EffectID = 5 });
        layout.Regions.WindSFX.Add(new() { Name = "authored-wind" });
        layout.Regions.EnvironmentMapPoints.Add(new() { Name = "authored-probe", Position = new(1, 5, 2) });
        MSBE.Region.EnvironmentMapEffectBox Box(string name, Vector3 at, float size) => new()
            { Name = name, Position = at, Shape = new MSB.Shape.Box { Width = size, Height = size, Depth = size }, PointLightMult = 1.5f };
        layout.Regions.EnvironmentMapEffectBoxes.Add(Box("authored-outdoors", new(0, -50, 0), 100));
        layout.Regions.EnvironmentMapEffectBoxes.Add(Box("authored-room", new(3, 0, 3), 4));
        layout.Regions.SpawnPoints.Add(new() { Name = "authored-spawn", Position = new(-3.2f, 40.1f, 7) });
        layout.Regions.SpawnPoints.Add(new() { Name = "authored-elsewhere", Position = new(90, 9, 9) });
        var native = layout.Write();

        FirelinkMapWriter.IsolateWorld(layout);
        var isolated = MSBE.Read(layout.Write());
        if (isolated.Parts.MapPieces.Single().GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease
            || isolated.Parts.Enemies.Single().GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease
            || isolated.Parts.Players.Single().GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable
            || isolated.Regions.SFX.Count != 0 || isolated.Regions.WindSFX.Count != 0)
            throw new Exception("Host placements or effect regions remain, or the player was disabled");
        isolated.Parts.MapPieces.Single().GameEditionDisable = isolated.Parts.Enemies.Single().GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
        isolated.Regions.SFX.Add(new() { Name = "authored-effect", EffectID = 5 });
        isolated.Regions.WindSFX.Add(new() { Name = "authored-wind" });
        if (!isolated.Write().SequenceEqual(native)) throw new Exception("World isolation changed unrelated map fields");

        // A translated effect is placed through a copy of an installed effect region, enabled and unowned.
        var placed = MSBE.Read(native);
        var effectTemplate = new MSBE.Region.SFX { Name = "authored-template", EffectID = 5, StartDisabled = 1, EntityID = 77, ActivationPartName = "authored-part", Position = new(9, 9, 9) };
        FirelinkMapWriter.AddEffect(placed, effectTemplate, "aog_authored_fire", 990100300, new(1, 2, 3), new(0, 10, 0));
        var fire = MSBE.Read(placed.Write()).Regions.SFX.Single(r => r.Name == "aog_authored_fire");
        if (fire.EffectID != 990100300 || fire.Position != new Vector3(1, 2, 3) || fire.Rotation != new Vector3(0, 10, 0) || fire.StartDisabled != 0 || fire.EntityID != 0
            || fire.ActivationPartName is not null || fire.Shape is not MSB.Shape.Point || effectTemplate.EffectID != 5)
            throw new Exception("Placed effect region differs or changed its template");
        try { FirelinkMapWriter.AddEffect(placed, effectTemplate, "aog_authored_fire", 1, default, default); throw new Exception("Duplicate effect region was accepted"); }
        catch (InvalidDataException) { }

        // A talking character is an unseen host controller copied from an installed one, with its own identities and talk script.
        var spoken = MSBE.Read(native);
        var host = spoken.Parts.Enemies.Single();
        host.EntityID = 500; host.InstanceID = 4; host.TalkID = 1000; host.EntityGroupIDs[0] = 9;
        FirelinkMapWriter.AddTalker(spoken, host, "aog_authored_talker", 600, 100190000, new(4, 5, 6), new(0, 30, 0));
        var talker = MSBE.Read(spoken.Write()).Parts.Enemies.Single(p => p.Name == "aog_authored_talker");
        if (talker.EntityID != 600 || talker.TalkID != 100190000 || talker.InstanceID != 5 || talker.ModelName != host.ModelName || talker.Position != new Vector3(4, 5, 6)
            || talker.Rotation != new Vector3(0, 30, 0) || talker.CollisionPartName is not null || talker.EntityGroupIDs.Any(g => g != 0)
            || talker.GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable || host.TalkID != 1000)
            throw new Exception("Talking controller differs or changed its template");
        foreach (var (name, entity) in new[] { ("aog_authored_talker", 601u), ("aog_other", 600u), ("aog_other", 500u) })
        {
            try { FirelinkMapWriter.AddTalker(spoken, host, name, entity, 5, default, default); throw new Exception("An occupied talker identity was accepted"); }
            catch (InvalidDataException) { }
        }

        // An enemy is an installed enemy placement moved to where the source stands one, with a build-owned row, the behaviour
        // asked for and the built collision it stands on; nothing else of the installed placement's script ties is kept.
        var fought = MSBE.Read(native);
        fought.Models.Collisions.Add(new() { Name = "h800005" }); fought.Parts.Collisions.Add(new() { Name = "h800005_0000", ModelName = "h800005" });
        // The installed placement comes from another map, with ties to that map's own parts.
        var soldier = new MSBE.Part.Enemy { Name = "c4300_9002", ModelName = "c4300", EntityID = 500, InstanceID = 4, NPCParamID = 111, ThinkParamID = 222, TalkID = 1000,
            WalkRouteName = "authored-route", CollisionPartName = "authored-collision" };
        soldier.EntityGroupIDs[0] = 9;
        FirelinkMapWriter.AddEnemy(fought, soldier, "aog_authored_enemy", 43008000, 43000000, "h800005_0000", new(4, 5, 6), new(0, 30, 0));
        FirelinkMapWriter.AddEnemy(fought, soldier, "aog_authored_enemy_2", 43008001, 43000000, "h800005_0000", new(7, 5, 6), new(0, 40, 0), 100190001);
        var enemies = MSBE.Read(fought.Write()).Parts.Enemies.Where(p => p.Name.StartsWith("aog_authored_enemy")).OrderBy(p => p.Name).ToArray();
        if (enemies.Length != 2 || enemies[0].NPCParamID != 43008000 || enemies[0].ThinkParamID != 43000000 || enemies[0].CollisionPartName != "h800005_0000" || enemies[0].EntityID != 0
            || enemies[0].TalkID != 0 || enemies[1].TalkID != 100190001 || enemies[0].WalkRouteName is not null || enemies[0].EntityGroupIDs.Any(g => g != 0) || enemies[0].Position != new Vector3(4, 5, 6) || enemies[0].Rotation != new Vector3(0, 30, 0)
            || enemies[0].InstanceID != 0 || enemies[1].InstanceID != 1 || enemies[0].ModelName != "c4300" || MSBE.Read(fought.Write()).Models.Enemies.Count(m => m.Name == "c4300") != 1 || enemies[0].GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable
            || enemies[0].Unk1.DisplayGroups.Any(g => g != uint.MaxValue) || soldier.NPCParamID != 111 || soldier.WalkRouteName != "authored-route")
            throw new Exception("Placed enemy differs or changed its template");
        // A placement may be of another model than its template's: a character the build itself writes. Its instances count from nought.
        FirelinkMapWriter.AddEnemy(fought, soldier, "aog_authored_own", 43008002, 43000000, "h800005_0000", new(1, 5, 6), default, 0, "c6405");
        FirelinkMapWriter.AddEnemy(fought, soldier, "aog_authored_own_2", 43008002, 43000000, "h800005_0000", new(2, 5, 6), default, 0, "c6405");
        var owned = MSBE.Read(fought.Write());
        if (!owned.Parts.Enemies.Where(p => p.Name.StartsWith("aog_authored_own")).OrderBy(p => p.Name).Select(p => (p.ModelName, p.InstanceID)).SequenceEqual([("c6405", 0), ("c6405", 1)]) || owned.Models.Enemies.Count(m => m.Name == "c6405") != 1 || soldier.ModelName != "c4300")
            throw new Exception("A placement of a build-owned model differs or changed its template");
        foreach (var (name, collision) in new[] { ("aog_authored_enemy", "h800005_0000"), ("aog_other_enemy", "") })
        {
            try { FirelinkMapWriter.AddEnemy(fought, soldier, name, 1, 2, collision, default, default); throw new Exception("An enemy with an occupied name or no collision was accepted"); }
            catch (InvalidDataException) { }
        }

        // Host environment lighting follows the scenery: probes and the one enclosing outdoor volume move, rooms stay.
        var lit = MSBE.Read(native);
        FirelinkMapWriter.MoveEnvironment(lit, new(0, 200, 0), new(-3, 240, 7), new(0, 145, 0));
        lit = MSBE.Read(lit.Write());
        if (lit.Regions.EnvironmentMapPoints.Single().Position != new Vector3(1, 205, 2)
            || lit.Regions.EnvironmentMapEffectBoxes.Single(b => b.Name == "authored-outdoors").Position != new Vector3(0, 150, 0)
            || lit.Regions.EnvironmentMapEffectBoxes.Single(b => b.Name == "authored-room").Position != new Vector3(3, 0, 3)
            || lit.Regions.EnvironmentMapEffectBoxes.Any(b => b.PointLightMult != 3)
            || lit.Regions.SpawnPoints.Single(r => r.Name == "authored-spawn").Position != new Vector3(-3, 240, 7)
            || lit.Regions.SpawnPoints.Single(r => r.Name == "authored-spawn").Rotation != new Vector3(0, 145, 0)
            || lit.Regions.SpawnPoints.Single(r => r.Name == "authored-elsewhere").Position != new Vector3(90, 9, 9))
            throw new Exception("Environment regions or the spawn region did not follow the scenery");
        var ambiguous = MSBE.Read(native);
        ambiguous.Regions.EnvironmentMapEffectBoxes.Add(Box("authored-second-outdoors", new(0, -50, 0), 100));
        Reject(() => FirelinkMapWriter.MoveEnvironment(ambiguous, new(0, 200, 0), new(-3, 240, 7), Vector3.Zero));
        Reject(() => FirelinkMapWriter.MoveEnvironment(MSBE.Read(native), new(0, 200, 0), new(50, 240, 7), Vector3.Zero));

        FirelinkMapWriter.SceneryPlacement[] placements = [
            new("wall-b", "wall", new(10, 0, 0), new(0, 90, 0), new(1, 1, 1)),
            new("arch-a", "arch", new(0, 5, 0), Vector3.Zero, new(2, 2, 2)),
            new("wall-a", "wall", new(-10, 0, 0), Vector3.Zero, new(1, 1, 1)),
        ];
        var before = layout.Write();
        var scenery = FirelinkMapWriter.AddScenery(layout, placements, new(100, 200, 300));
        var output = layout.Write();
        var check = MSBE.Read(output);
        if (!scenery.Models.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).SequenceEqual(new[] { ("arch", "810000"), ("wall", "810001") })
            || !scenery.Placements.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).SequenceEqual(
                new[] { ("m810000_0000", "arch-a"), ("m810001_0000", "wall-a"), ("m810001_0001", "wall-b") }))
            throw new Exception("Scenery identities are not ordered by source model and placement name");
        var wall = check.Parts.MapPieces.Single(p => p.Name == "m810001_0001");
        if (wall.ModelName != "m810001" || wall.Position != new Vector3(110, 200, 300) || wall.Rotation != new Vector3(0, 90, 0)
            || check.Parts.MapPieces.Single(p => p.Name == "m810000_0000").Scale != new Vector3(2, 2, 2)
            || wall.EntityID != 0 || wall.GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable
            || wall.Unk1.DrawGroups.Any(v => v != uint.MaxValue) || wall.Unk1.DisplayGroups.Any(v => v != uint.MaxValue)
            || check.Models.MapPieces.Count != 3)
            throw new Exception("Scenery placement transform, identity or visibility differs");
        var repeated = MSBE.Read(before);
        FirelinkMapWriter.AddScenery(repeated, placements.Reverse().ToArray(), new(100, 200, 300));
        if (!output.SequenceEqual(repeated.Write())) throw new Exception("Scenery map depends on placement order");
        Reject(() => FirelinkMapWriter.AddScenery(check, placements, Vector3.Zero));
        Reject(() => FirelinkMapWriter.AddScenery(MSBE.Read(before), [placements[0], placements[0]], Vector3.Zero));
        check.Parts.MapPieces.RemoveAll(p => p.Name.StartsWith("m81", StringComparison.Ordinal));
        check.Models.MapPieces.RemoveAll(m => m.Name.StartsWith("m81", StringComparison.Ordinal));
        if (!before.SequenceEqual(check.Write())) throw new Exception("Scenery changed unrelated map fields");
        Console.WriteLine("PASS: host world isolation keeps the player and removes effect regions; environment lighting follows the scenery; source scenery placements are ordered, transformed, enabled and reject occupied or duplicate identities");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Accepted occupied or duplicate scenery identities");
    }
}
