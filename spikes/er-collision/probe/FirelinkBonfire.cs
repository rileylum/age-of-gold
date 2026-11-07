using System.Numerics;
using SoulsFormats;

static class FirelinkBonfire
{
    /// <summary>Host resting-point parts and registration supplied by the install reader.</summary>
    public record Template(MSBE.Part.Asset Marker, MSBE.Part.Enemy Controller,
        MSBE.Region.SpawnPoint Respawn, MSBE.Model.Asset MarkerModel,
        MSBE.Model.Enemy ControllerModel, EMEVD.Instruction Registration);
    /// <summary>A translated source resting point and its reserved host identity.</summary>
    public record Placement(string Name, uint Entity, int Flag, Vector3 Position, Vector3 Rotation);

    /// <summary>Place a resting point and its respawn region using a host template read from the install.</summary>
    public static (byte[] Map, byte[] Events) Apply(byte[] map, byte[] events, Template template, Placement placement)
    {
        var layout = MSBE.Read(map); var script = EMEVD.Read(events);
        var registration = template.Registration;
        if (registration.Bank != 2009 || registration.ID != 3 || registration.ArgData.Length != 24
            || BitConverter.ToUInt32(registration.ArgData, 4) != template.Marker.EntityID
            || template.Marker.ModelName != template.MarkerModel.Name
            || template.Controller.ModelName != template.ControllerModel.Name)
            throw new InvalidDataException("Bonfire template bindings do not agree");
        if (placement.Flag <= 0 || script.Events.SelectMany(e => e.Instructions)
            .Where(i => i.Bank == 2009 && i.ID == 3)
            .Any(i => i.ArgData.Length != 24 || BitConverter.ToInt32(i.ArgData) == placement.Flag))
            throw new InvalidDataException("Bonfire activation flag is invalid or already registered");
        bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        if (!new[] { placement.Position, placement.Rotation, template.Marker.Position, template.Marker.Rotation,
            template.Controller.Position, template.Controller.Rotation, template.Respawn.Position, template.Respawn.Rotation }.All(Finite))
            throw new InvalidDataException("Bonfire positions and rotations must be finite");
        // lazy: upright placements only until a source resting point needs pitch or roll.
        if (placement.Rotation.X != 0 || placement.Rotation.Z != 0
            || template.Marker.Rotation.X != 0 || template.Marker.Rotation.Z != 0)
            throw new InvalidDataException("Bonfire placement requires upright yaw rotation");
        uint Relocate(uint id) => checked((uint)((long)id - template.Marker.EntityID + placement.Entity));
        var identities = new HashSet<uint> { placement.Entity, Relocate(template.Controller.EntityID), Relocate(template.Respawn.EntityID) };
        var occupied = layout.Parts.GetEntries().Select(p => p.EntityID)
            .Concat(layout.Regions.GetEntries().Select(r => r.EntityID))
            .Concat(layout.Events.GetEntries().Select(e => e.EntityID));
        if (identities.Count != 3 || identities.Contains(0) || identities.Contains(uint.MaxValue)
            || occupied.Any(identities.Contains))
            throw new InvalidDataException("Bonfire identities are invalid or occupied");
        var names = new[] { placement.Name + "_marker", placement.Name + "_controller", placement.Name + "_respawn" };
        if (string.IsNullOrWhiteSpace(placement.Name) || layout.Parts.GetEntries().Select(p => p.Name)
            .Concat(layout.Regions.GetEntries().Select(r => r.Name)).Any(names.Contains))
            throw new InvalidDataException("Bonfire placement name is empty or occupied");
        var constructor = script.Events.Single(e => e.ID == 0);
        var yaw = placement.Rotation.Y - template.Marker.Rotation.Y;
        var rotation = Matrix4x4.CreateRotationY(yaw * MathF.PI / 180);
        Vector3 Translate(Vector3 position) => placement.Position + Vector3.Transform(position - template.Marker.Position, rotation);
        var marker = (MSBE.Part.Asset)template.Marker.DeepCopy();
        var controller = (MSBE.Part.Enemy)template.Controller.DeepCopy();
        var respawn = (MSBE.Region.SpawnPoint)template.Respawn.DeepCopy();
        marker.Name = names[0]; marker.EntityID = placement.Entity;
        marker.Position = placement.Position; marker.Rotation = placement.Rotation;
        marker.UnkPartNames = new string[6]; marker.UnkT54PartName = null;
        marker.Unk1.DrawGroups = Enumerable.Repeat(uint.MaxValue, 8).ToArray();
        marker.Unk1.DisplayGroups = Enumerable.Repeat(uint.MaxValue, 8).ToArray();
        controller.Name = names[1]; controller.EntityID = Relocate(template.Controller.EntityID);
        controller.Position = Translate(template.Controller.Position); controller.Rotation += new Vector3(0, yaw, 0);
        controller.CollisionPartName = null; controller.WalkRouteName = null;
        controller.Unk1.DrawGroups = Enumerable.Repeat(uint.MaxValue, 8).ToArray();
        controller.Unk1.DisplayGroups = Enumerable.Repeat(uint.MaxValue, 8).ToArray();
        respawn.Name = names[2]; respawn.EntityID = Relocate(template.Respawn.EntityID);
        respawn.Position = Translate(template.Respawn.Position); respawn.Rotation += new Vector3(0, yaw, 0);
        respawn.RegionID = checked(layout.Regions.GetEntries().Select(r => r.RegionID).DefaultIfEmpty(-1).Max() + 1);
        respawn.ActivationPartName = null;
        var addedModels = new HashSet<string>();
        foreach (var model in new MSBE.Model[] { template.MarkerModel, template.ControllerModel })
        {
            var existing = layout.Models.GetEntries().SingleOrDefault(m => m.Name == model.Name);
            if (existing is not null)
            {
                if (existing.GetType() != model.GetType()) throw new InvalidDataException("Bonfire model has the wrong kind");
                continue;
            }
            addedModels.Add(model.Name);
            if (model is MSBE.Model.Asset asset) layout.Models.Assets.Add((MSBE.Model.Asset)asset.DeepCopy());
            else layout.Models.Enemies.Add((MSBE.Model.Enemy)model.DeepCopy());
        }
        layout.Parts.Assets.Add(marker); layout.Parts.Enemies.Add(controller); layout.Regions.SpawnPoints.Add(respawn);
        var instruction = new EMEVD.Instruction(registration.Bank, registration.ID, registration.ArgData.ToArray())
        { Layer = registration.Layer };
        BitConverter.GetBytes(placement.Flag).CopyTo(instruction.ArgData, 0);
        BitConverter.GetBytes(placement.Entity).CopyTo(instruction.ArgData, 4);
        constructor.Instructions.Add(instruction);
        var mapOutput = layout.Write(); var eventOutput = script.Write();
        var actual = MSBE.Read(mapOutput); var actualEvents = EMEVD.Read(eventOutput);
        if (actual.Parts.Assets.Single(p => p.EntityID == marker.EntityID).Position != marker.Position
            || actual.Parts.Enemies.Single(p => p.EntityID == controller.EntityID).Position != controller.Position
            || actual.Regions.SpawnPoints.Single(r => r.EntityID == respawn.EntityID).Position != respawn.Position
            || actualEvents.Events.Single(e => e.ID == 0).Instructions.Last().Layer != instruction.Layer
            || !actualEvents.Events.Single(e => e.ID == 0).Instructions.Last().ArgData.AsSpan().SequenceEqual(instruction.ArgData))
            throw new InvalidDataException("Bonfire placement or registration did not round trip");
        actual.Parts.Assets.RemoveAll(p => p.EntityID == marker.EntityID);
        actual.Parts.Enemies.RemoveAll(p => p.EntityID == controller.EntityID);
        actual.Regions.SpawnPoints.RemoveAll(r => r.EntityID == respawn.EntityID);
        actual.Models.Assets.RemoveAll(m => addedModels.Contains(m.Name));
        actual.Models.Enemies.RemoveAll(m => addedModels.Contains(m.Name));
        var actualInstructions = actualEvents.Events.Single(e => e.ID == 0).Instructions;
        actualInstructions.RemoveAt(actualInstructions.Count - 1);
        if (!actual.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(MSBE.Read(map).Write(new DCX.NoCompressionInfo()))
            || !actualEvents.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(EMEVD.Read(events).Write(new DCX.NoCompressionInfo())))
            throw new InvalidDataException("Bonfire placement changed unrelated map or event data");
        return (mapOutput, eventOutput);
    }

    /// <summary>Check placement, registration and respawn together through authored serialized inputs.</summary>
    public static void Check()
    {
        var map = new MSBE();
        map.Models.Assets.Add(new() { Name = "our_retained_model" });
        map.Parts.Assets.Add(new() { Name = "our_retained_part", ModelName = "our_retained_model", EntityID = 41 });
        var events = new EMEVD(EMEVD.Game.DarkSouls3);
        var constructor = new EMEVD.Event(0);
        constructor.Instructions.Add(new(2000, 0, new object[] { 0, 47, 0 }));
        events.Events.Add(constructor);
        var template = new Template(
            new() { Name = "our_marker", ModelName = "our_marker_model", EntityID = 501,
                Position = new(10, 2, 20), Rotation = new(0, 90, 0) },
            new() { Name = "our_controller", ModelName = "our_controller_model", EntityID = 491,
                Position = new(12, 2, 20), Rotation = new(0, 90, 0) },
            new() { Name = "our_respawn", EntityID = 521, RegionID = 3,
                Position = new(10, 1.9f, 22), Rotation = new(0, 120, 0) },
            new() { Name = "our_marker_model" }, new() { Name = "our_controller_model" },
            new EMEVD.Instruction(2009, 3, new object[] { 71, (uint)501, 1.5f, 3.5f, 13, 4.5f }) { Layer = 5 });
        var placement = new Placement("our_clearing", 701, 73, new(30, 5, -10), new(0, 180, 0));
        var beforeMap = map.Write(new DCX.NoCompressionInfo());
        var beforeEvents = events.Write(new DCX.NoCompressionInfo());
        var output = Apply(beforeMap, beforeEvents, template, placement);
        var actual = MSBE.Read(output.Map);
        var marker = actual.Parts.Assets.SingleOrDefault(p => p.EntityID == 701);
        var controller = actual.Parts.Enemies.SingleOrDefault(p => p.EntityID == 691);
        var respawn = actual.Regions.SpawnPoints.SingleOrDefault(r => r.EntityID == 721);
        if (marker is null || controller is null || respawn is null)
            throw new InvalidDataException("Bonfire needs its marker, controller and separate respawn region");
        if (Vector3.Distance(marker.Position, new(30, 5, -10)) > 0.0001f
            || Vector3.Distance(controller.Position, new(30, 5, -12)) > 0.0001f
            || Vector3.Distance(respawn.Position, new(32, 4.9f, -10)) > 0.0001f
            || respawn.Rotation != new Vector3(0, 210, 0))
            throw new InvalidDataException("Bonfire offsets and facing must rotate with the placement");
        var readEvents = EMEVD.Read(output.Events);
        var instructions = readEvents.Events.Single().Instructions;
        var registration = instructions.Last();
        var expected = new EMEVD.Instruction(2009, 3, new object[] { 73, (uint)701, 1.5f, 3.5f, 13, 4.5f });
        if (instructions.Count != 2 || registration.Bank != expected.Bank || registration.ID != expected.ID
            || registration.Layer != 5
            || !registration.ArgData.AsSpan().SequenceEqual(expected.ArgData))
            throw new InvalidDataException("Bonfire must register the placed marker and activation flag");
        actual.Parts.Assets.Remove(marker); actual.Parts.Enemies.Remove(controller);
        actual.Regions.SpawnPoints.Remove(respawn);
        actual.Models.Assets.RemoveAll(m => m.Name == template.MarkerModel.Name);
        actual.Models.Enemies.RemoveAll(m => m.Name == template.ControllerModel.Name);
        instructions.RemoveAt(instructions.Count - 1);
        if (!actual.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(beforeMap)
            || !readEvents.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(beforeEvents))
            throw new InvalidDataException("Bonfire placement changed unrelated map or event data");
        var repeat = Apply(beforeMap, beforeEvents, template, placement);
        if (!repeat.Map.AsSpan().SequenceEqual(output.Map) || !repeat.Events.AsSpan().SequenceEqual(output.Events))
            throw new InvalidDataException("Bonfire placement must be deterministic");
        bool occupied = false;
        try { Apply(output.Map, beforeEvents, template, placement); }
        catch (InvalidDataException) { occupied = true; }
        if (!occupied) throw new InvalidDataException("A second bonfire must not replace existing identities");
        constructor.Instructions.Add(new(2009, 3, new object[] { 73, (uint)43, 1.5f, 3.5f, 13, 4.5f }));
        bool duplicateFlag = false;
        try { Apply(beforeMap, events.Write(new DCX.NoCompressionInfo()), template, placement); }
        catch (InvalidDataException) { duplicateFlag = true; }
        if (!duplicateFlag) throw new InvalidDataException("Two bonfires must not share an activation flag");
        Console.WriteLine("PASS: bonfire registration, transformed placement and respawn; unrelated data preserved");
    }
}
