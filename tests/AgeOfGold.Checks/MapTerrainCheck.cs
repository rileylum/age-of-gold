using System.Numerics;
using AgeOfGold.Collision;
using AgeOfGold.Maps;
using SoulsFormats;

internal static class MapTerrainCheck
{
    internal static void Run()
    {
        var layout = new MSBE();
        layout.Models.Players.Add(new() { Name = "c0000" });
        layout.Parts.Players.Add(new() { Name = "c0000_0000", ModelName = "c0000", Position = new(100,20,300) });
        layout.Models.Collisions.Add(new() { Name = "h020100" });
        layout.Parts.Collisions.Add(new() { Name = "authored-template", ModelName = "h020100",
            Position = new(7,8,9), HitFilterID = MSBE.Part.Collision.HitFilterType.Standard });
        var before = layout.Write();
        var body = new SourceCollisionReader.Body("authored", [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1], [
            new([[-2,0,-2,0],[2,0,-2,0],[0,0,2,0]], [[0,1,2,0]], 16, 0,
                [0,0,0,0], [0,0,0,1], [1,1,1,0], 0, [0,0,0,0]),
        ]);
        var solid = new SourceCollisionReader.Placement("authored-solid", "high", "low", "solid", false,
            [0,0,0], [0,0,0], [1,1,1], [1,4,16,64]);
        var source = new SourceCollisionReader.Scene(1, new(StringComparer.Ordinal) { ["high"] = body, ["low"] = body },
            [solid, solid with { Name = "authored-water", Role = "water" }, solid with { Name = "authored-disabled", Disabled = true }],
            new([0,0,0], [0,75,0]));
        var terrain = FirelinkMapWriter.AddTerrain(layout, source);
        var output = layout.Write();
        var check = MSBE.Read(output);
        var player = check.Parts.Players.Single();
        if (player.Position != new Vector3(100,220.3f,300) || player.Rotation != new Vector3(0,75,0)
            || terrain.Offset != new Vector3(100,220,300) || terrain.Spawn != player.Position
            || terrain.High.Length != 1 || terrain.Low.Length != 1
            || !terrain.Excluded.Select(p => p.Name).SequenceEqual(new[] { "authored-water", "authored-disabled" }))
            throw new Exception("Terrain placement, spawn or special-role accounting differs");
        var collision = check.Parts.Collisions.Single(p => p.ModelName == "h800000");
        if (collision.Position != Vector3.Zero || collision.Rotation != Vector3.Zero || collision.Scale != Vector3.One
            || collision.Unk1.DisplayGroups.Any(v => v != uint.MaxValue)
            || collision.Unk1.DrawGroups.Any(v => v != uint.MaxValue) || collision.Unk1.CollisionMask.Any(v => v != uint.MaxValue)
            || collision.EntityID != 0 || collision.EntityGroupIDs.Any(v => v != 0)
            || collision.HitFilterID != MSBE.Part.Collision.HitFilterType.Standard)
            throw new Exception("Terrain collision placement or visibility differs");
        var repeated = MSBE.Read(before);
        FirelinkMapWriter.AddTerrain(repeated, source);
        if (!output.SequenceEqual(repeated.Write())) throw new Exception("Terrain map repeat differs");
        Reject(() => FirelinkMapWriter.AddTerrain(check, source), "occupied model identities");
        var unsupported = MSBE.Read(before);
        unsupported.Parts.Collisions[0].HitFilterID = MSBE.Part.Collision.HitFilterType.CameraOnly;
        Reject(() => FirelinkMapWriter.AddTerrain(unsupported, source), "non-solid host template");
        player.Position = new(100,20,300); player.Rotation = Vector3.Zero;
        check.Parts.Collisions.Remove(collision);
        check.Models.Collisions.RemoveAll(m => m.Name == "h800000");
        if (!before.SequenceEqual(check.Write())) throw new Exception("Unrelated map fields changed");
        Console.WriteLine("PASS: terrain map supports source spawn, keeps terrain always visible, retains host fields, reports excluded roles and rejects occupied identities");
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Accepted " + reason);
    }
}
