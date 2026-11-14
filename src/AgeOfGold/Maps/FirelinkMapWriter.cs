using System.Numerics;
using AgeOfGold.Collision;
using SoulsFormats;

namespace AgeOfGold.Maps;

internal static class FirelinkMapWriter
{
    /// <param name="Arrival">The source collision placement that supports the spawn.</param>
    /// <param name="Sources">The source collision placement behind each built part, in the parts' order.</param>
    internal sealed record Terrain(Vector3 Offset, Vector3 Spawn, HostCollisionWriter.Part[] High,
        HostCollisionWriter.Part[] Low, SourceCollisionReader.Placement[] Excluded, string Arrival, string[] Sources)
    {
        /// <summary>The source collision placement whose surface lies nearest beneath a point in the build.</summary>
        internal string Support(Vector3 point)
        {
            string? best = null; float height = float.NegativeInfinity;
            for (int i = 0; i < Low.Length; i++)
            {
                float ground;
                // Most placements have no surface under the point at all.
                try { ground = TerrainGeometry.GroundBelow([Low[i].Geometry], point + new Vector3(0, 0.5f, 0)); }
                catch (InvalidDataException) { continue; }
                if (ground > height) (best, height) = (Sources[i], ground);
            }
            return best ?? throw new InvalidDataException("No terrain beneath the requested point");
        }
    }

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
        bool Supports(TerrainGeometry.Mesh mesh)
        {
            // Most placements have no surface under the spawn at all.
            try { return TerrainGeometry.GroundBelow([mesh], sourceSpawn + offset + new Vector3(0, 2, 0)) == floor; }
            catch (InvalidDataException) { return false; }
        }
        var supports = placements.Where((_, i) => Supports(low[i].Geometry)).ToArray();
        if (supports.Length != 1) throw new InvalidDataException("Source spawn is supported by several collision placements");
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
        return new(offset, spawn, high, low, excluded, supports[0].Name, placements.Select(p => p.Name).ToArray());
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
        // Host map effects and wind belong to the host's scenery; a start-disabled effect can still be switched on by host scripts.
        layout.Regions.SFX.Clear();
        layout.Regions.WindSFX.Clear();
    }

    /// <summary>
    /// Raises the host's environment lighting to the scenery: every probe and the one outdoor volume enclosing the
    /// others move by <paramref name="displacement"/>; room volumes stay below. The host's arrival region moves to the spawn.
    /// </summary>
    internal static void MoveEnvironment(MSBE layout, Vector3 displacement, Vector3 spawn, Vector3 spawnRotation)
    {
        var boxes = layout.Regions.EnvironmentMapEffectBoxes;
        foreach (var box in boxes)
            if (box.Shape is not MSB.Shape.Box s || !(s.Width > 0 && s.Height > 0 && s.Depth > 0) || box.Rotation.X != 0 || box.Rotation.Z != 0)
                throw new InvalidDataException("Expected upright environment boxes");
        // A box's origin is the centre of its floor.
        Vector3[] Corners(MSBE.Region.EnvironmentMapEffectBox box)
        {
            var s = (MSB.Shape.Box)box.Shape;
            var rotation = Matrix4x4.CreateRotationY(box.Rotation.Y * MathF.PI / 180);
            return (from x in new[] { -s.Width / 2, s.Width / 2 } from y in new[] { 0f, s.Height } from z in new[] { -s.Depth / 2, s.Depth / 2 }
                    select Vector3.Transform(new(x, y, z), rotation) + box.Position).ToArray();
        }
        bool Contains(MSBE.Region.EnvironmentMapEffectBox box, Vector3 point)
        {
            var s = (MSB.Shape.Box)box.Shape;
            var p = Vector3.Transform(point - box.Position, Matrix4x4.CreateRotationY(-box.Rotation.Y * MathF.PI / 180));
            return Math.Abs(p.X) <= s.Width / 2 + .001f && Math.Abs(p.Z) <= s.Depth / 2 + .001f && p.Y >= -.001f && p.Y <= s.Height + .001f;
        }
        var outdoors = boxes.Where(b => boxes.Where(other => other != b).SelectMany(Corners).All(p => Contains(b, p))).ToArray();
        if (boxes.Count < 2 || outdoors.Length != 1) throw new InvalidDataException("Expected one outdoor environment volume enclosing the others");
        outdoors[0].Position += displacement;
        // lazy: the accepted playtest's doubled fill for host-lit characters; source environment light for characters is not translated yet.
        foreach (var box in boxes) box.PointLightMult *= 2;
        foreach (var point in layout.Regions.EnvironmentMapPoints) point.Position += displacement;
        // The host's arrival region sits at its own player start; other spawn regions belong to host events.
        var arrival = layout.Regions.SpawnPoints.Where(r => Vector3.Distance(r.Position, spawn - displacement) < 1).ToArray();
        if (arrival.Length != 1) throw new InvalidDataException("Expected one host spawn region at the host player start");
        arrival[0].Position = spawn; arrival[0].Rotation = spawnRotation;
    }

    /// <summary>Places an effect at a point, through a copy of an installed effect region.</summary>
    internal static void AddEffect(MSBE layout, MSBE.Region.SFX template, string name, int effect, Vector3 position, Vector3 rotation)
    {
        if (layout.Regions.GetEntries().Any(r => r.Name == name)) throw new InvalidDataException("Effect region identity is already occupied: " + name);
        var region = (MSBE.Region.SFX)template.DeepCopy();
        region.Name = name; region.EffectID = effect; region.StartDisabled = 0;
        region.Shape = new MSB.Shape.Point(); region.Position = position; region.Rotation = rotation;
        region.EntityID = 0; region.ActivationPartName = null;
        layout.Regions.SFX.Add(region);
    }

    /// <summary>Adds an unseen controller that runs a talk script where a source character is spoken to, copied from an installed controller.</summary>
    internal static void AddTalker(MSBE layout, MSBE.Part.Enemy template, string name, uint entity, int talk, Vector3 position, Vector3 rotation)
    {
        if (layout.Parts.GetEntries().Any(p => p.Name == name || p.EntityID == entity) || layout.Regions.GetEntries().Any(r => r.EntityID == entity))
            throw new InvalidDataException("Talker identity is already occupied: " + name);
        var talker = (MSBE.Part.Enemy)template.DeepCopy();
        talker.Name = name; talker.EntityID = entity; talker.TalkID = talk;
        // Placements of one model are told apart by instance.
        talker.InstanceID = layout.Parts.Enemies.Where(p => p.ModelName == talker.ModelName).Select(p => p.InstanceID).DefaultIfEmpty(-1).Max() + 1;
        talker.Position = position; talker.Rotation = rotation;
        talker.CollisionPartName = null; talker.WalkRouteName = null; Array.Fill(talker.EntityGroupIDs, 0u);
        talker.GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
        layout.Parts.Enemies.Add(talker);
    }

    /// <summary>Adds an enemy where the source stands one, copied from an installed placement of the host character that carries it.</summary>
    /// <param name="collision">The built collision part it stands on; the host does not show an enemy without one.</param>
    internal static void AddEnemy(MSBE layout, MSBE.Part.Enemy template, string name, int row, int behaviour, string collision, Vector3 position, Vector3 rotation)
    {
        if (layout.Parts.GetEntries().Any(p => p.Name == name)) throw new InvalidDataException("Enemy identity is already occupied: " + name);
        if (string.IsNullOrEmpty(collision)) throw new InvalidDataException("An enemy needs the collision it stands on: " + name);
        var enemy = (MSBE.Part.Enemy)template.DeepCopy();
        enemy.Name = name; enemy.NPCParamID = row; enemy.ThinkParamID = behaviour; enemy.CollisionPartName = collision;
        enemy.Position = position; enemy.Rotation = rotation;
        // Placements of one model are told apart by instance.
        enemy.InstanceID = layout.Parts.Enemies.Where(p => p.ModelName == enemy.ModelName).Select(p => p.InstanceID).DefaultIfEmpty(-1).Max() + 1;
        // The installed placement's ties to its own map's scripts, talk and routes do not come along.
        enemy.EntityID = 0; enemy.TalkID = 0; enemy.WalkRouteName = null; Array.Fill(enemy.EntityGroupIDs, 0u);
        enemy.GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
        Array.Fill(enemy.Unk1.DisplayGroups, uint.MaxValue); Array.Fill(enemy.Unk1.DrawGroups, uint.MaxValue);
        if (!layout.Models.Enemies.Any(m => m.Name == enemy.ModelName)) layout.Models.Enemies.Add(new MSBE.Model.Enemy { Name = enemy.ModelName });
        layout.Parts.Enemies.Add(enemy);
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
