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
        var native = layout.Write();

        FirelinkMapWriter.IsolateWorld(layout);
        var isolated = MSBE.Read(layout.Write());
        if (isolated.Parts.MapPieces.Single().GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease
            || isolated.Parts.Enemies.Single().GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease
            || isolated.Parts.Players.Single().GameEditionDisable != MSBE.Part.GameEditionDisableType.NeverDisable
            || isolated.Regions.SFX.Single().StartDisabled != 1)
            throw new Exception("Host placements or effects stayed enabled, or the player was disabled");
        isolated.Parts.MapPieces.Single().GameEditionDisable = isolated.Parts.Enemies.Single().GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
        isolated.Regions.SFX.Single().StartDisabled = 0;
        if (!isolated.Write().SequenceEqual(native)) throw new Exception("World isolation changed unrelated map fields");

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
        Console.WriteLine("PASS: host world isolation keeps the player; source scenery placements are ordered, transformed, enabled and reject occupied or duplicate identities");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Accepted occupied or duplicate scenery identities");
    }
}
