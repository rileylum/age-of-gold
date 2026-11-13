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
            collision.GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
            // lazy: always visible, as in the accepted playtest. Carrying the source's display groups hid the player in play;
            // they need a mapping onto the host's group bits first.
            collision.Unk1.DisplayGroups = new uint[8];
            Array.Fill(collision.Unk1.DisplayGroups, uint.MaxValue);
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

    internal sealed record SceneryPlacement(string Name, string Model, Vector3 Position, Vector3 Rotation, Vector3 Scale);

    /// <summary>Host model digits by source model, and source placement by host placement name.</summary>
    internal sealed record Scenery(SortedDictionary<string, string> Models, SortedDictionary<string, string> Placements);

    /// <summary>Disables every installed host placement and map effect except the player's, before translated content is added.</summary>
    internal static void IsolateWorld(MSBE layout)
    {
        foreach (var part in layout.Parts.GetEntries())
        {
            if (part is MSBE.Part.Player) continue;
            // An unknown placement kind could keep host content in the world unnoticed.
            if (part is not (MSBE.Part.MapPiece or MSBE.Part.Asset or MSBE.Part.Collision or MSBE.Part.ConnectCollision
                or MSBE.Part.Enemy or MSBE.Part.DummyEnemy or MSBE.Part.DummyAsset))
                throw new InvalidDataException("Unclassified host placement: " + part.Name);
            part.GameEditionDisable = MSBE.Part.GameEditionDisableType.DisableInRelease;
        }
        foreach (var effect in layout.Regions.SFX) effect.StartDisabled = 1;
    }

    internal static Scenery AddScenery(MSBE layout, IReadOnlyList<SceneryPlacement> placements, Vector3 offset)
    {
        var template = layout.Parts.MapPieces.FirstOrDefault(p => p.ModelName == "m000000")
            ?? throw new InvalidDataException("Host map lacks the scenery placement template");
        var templateModel = layout.Models.MapPieces.Single(m => m.Name == template.ModelName);
        if (placements.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != placements.Count)
            throw new InvalidDataException("Duplicate source scenery placement");
        var result = new Scenery(new(StringComparer.Ordinal), new(StringComparer.Ordinal));
        foreach (var (model, index) in placements.Select(p => p.Model).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select((m, i) => (m, i)))
        {
            string digits = (810000 + index).ToString("D6"), id = "m" + digits;
            if (layout.Models.MapPieces.Any(m => m.Name == id) || layout.Parts.MapPieces.Any(p => p.Name.StartsWith(id + "_", StringComparison.Ordinal)))
                throw new InvalidDataException("Scenery identity is already occupied: " + id);
            var added = (MSBE.Model.MapPiece)templateModel.DeepCopy();
            added.Name = id; added.SibPath = "";
            layout.Models.MapPieces.Add(added);
            result.Models.Add(model, digits);
            int instance = 0;
            foreach (var placement in placements.Where(p => p.Model == model).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var part = (MSBE.Part.MapPiece)template.DeepCopy();
                part.Name = $"{id}_{instance++:D4}"; part.ModelName = id; part.SibPath = "";
                part.Position = placement.Position + offset; part.Rotation = placement.Rotation; part.Scale = placement.Scale;
                part.EntityID = 0; Array.Fill(part.EntityGroupIDs, 0u);
                part.GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
                // lazy: always visible, as in the accepted playtest; source draw groups need a host group mapping before they can be carried.
                Array.Fill(part.Unk1.DisplayGroups, uint.MaxValue); Array.Fill(part.Unk1.DrawGroups, uint.MaxValue);
                Array.Fill(part.Unk1.CollisionMask, uint.MaxValue);
                layout.Parts.MapPieces.Add(part);
                result.Placements.Add(part.Name, placement.Name);
            }
        }
        return result;
    }

    private static Vector3 Vector(float[] value) => new(value[0], value[1], value[2]);
}
