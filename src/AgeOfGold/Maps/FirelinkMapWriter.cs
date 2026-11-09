using System.Numerics;
using AgeOfGold.Collision;
using SoulsFormats;

namespace AgeOfGold.Maps;

internal static class FirelinkMapWriter
{
    internal sealed record Terrain(Vector3 Offset, Vector3 Spawn, HostCollisionWriter.Part[] High,
        HostCollisionWriter.Part[] Low, SourceCollisionReader.Placement[] Excluded);

    internal static Terrain AddTerrain(MSBE layout, SourceCollisionReader.Scene source)
    {
        var player = layout.Parts.Players.Single(p => p.Name == "c0000_0000");
        var template = layout.Parts.Collisions.Single(p => p.ModelName == "h020100");
        var templateModel = layout.Models.Collisions.Single(m => m.Name == template.ModelName);
        if (template.HitFilterID != MSBE.Part.Collision.HitFilterType.Standard)
            throw new InvalidDataException("Host terrain placement requires a Standard hit filter");
        bool Included(SourceCollisionReader.Placement p) => p.Role == "solid" && !p.Disabled;
        var placements = source.Placements.Where(Included).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        var excluded = source.Placements.Where(p => !Included(p)).ToArray();
        var names = Enumerable.Range(800000, placements.Length).Select(i => i.ToString("D6")).ToArray();
        foreach (var name in names)
            if (layout.Models.Collisions.Any(m => m.Name == "h" + name) || layout.Models.MapPieces.Any(m => m.Name == "m" + name)
                || layout.Parts.Collisions.Any(p => p.Name == "h" + name + "_0000"))
                throw new InvalidDataException("Terrain identity is already occupied: " + name);
        var sourceSpawn = Vector(source.Spawn.Position);
        // Retain the checked placement above the host's starting map while later stages remove its scenery.
        var offset = player.Position + new Vector3(0,200,0) - sourceSpawn;
        var high = placements.Select((p, i) => new HostCollisionWriter.Part(names[i], TerrainGeometry.Place(source.Models[p.High], p, offset))).ToArray();
        var low = placements.Select((p, i) => new HostCollisionWriter.Part(names[i], TerrainGeometry.Place(source.Models[p.Low], p, offset))).ToArray();
        float floor = TerrainGeometry.GroundBelow(low.Select(p => p.Geometry), sourceSpawn + offset + new Vector3(0,2,0));
        if (Math.Abs(floor - sourceSpawn.Y - offset.Y) > 3)
            throw new InvalidDataException("Source spawn is not near ordinary solid terrain");
        var spawn = new Vector3(sourceSpawn.X + offset.X, floor + 0.3f, sourceSpawn.Z + offset.Z);
        for (int i = 0; i < placements.Length; i++)
        {
            var collision = (MSBE.Part.Collision)template.DeepCopy();
            collision.Name = "h" + names[i] + "_0000";
            collision.ModelName = "h" + names[i]; collision.SibPath = "";
            collision.Position = Vector3.Zero; collision.Rotation = Vector3.Zero; collision.Scale = Vector3.One;
            collision.EntityID = 0; Array.Fill(collision.EntityGroupIDs, 0u);
            collision.Unk1.DisplayGroups = new uint[8];
            placements[i].DisplayGroups.CopyTo(collision.Unk1.DisplayGroups, 0);
            Array.Fill(collision.Unk1.DrawGroups, uint.MaxValue);
            Array.Fill(collision.Unk1.CollisionMask, uint.MaxValue);
            var model = (MSBE.Model.Collision)templateModel.DeepCopy();
            model.Name = collision.ModelName; model.SibPath = "";
            layout.Models.Collisions.Add(model);
            layout.Parts.Collisions.Add(collision);
        }
        player.Position = spawn;
        player.Rotation = Vector(source.Spawn.Rotation);
        return new(offset, spawn, high, low, excluded);
    }

    private static Vector3 Vector(float[] value) => new(value[0], value[1], value[2]);
}
