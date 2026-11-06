using System.Numerics;
using SoulsFormats;

// The opening cutscene overrides the MSB player placement; arrival must follow that event.
static class FirelinkArrival
{
    /// <summary>Move the opening respawn region and transfer the player after the introduction.</summary>
    public static void Apply(EMEVD events, MSBE layout, MSBE.Part.Player player)
    {
        var (opening, character, region) = Bind(events, layout);
        region.Position = player.Position;
        region.Rotation = player.Rotation;
        // Appending preserves existing instruction indices, branch distances and parameter offsets.
        opening.Instructions.Add(new EMEVD.Instruction(2004, 41, new object[] { character, (byte)1, region.EntityID, -1 }));
    }

    /// <summary>Verify that the written event adds only the arrival warp and targets the supported respawn.</summary>
    public static void Verify(byte[] original, byte[] written, MSBE layout, MSBE.Part.Player player)
    {
        var before = EMEVD.Read(original);
        var after = EMEVD.Read(written);
        var (opening, character, region) = Bind(after, layout);
        var warp = opening.Instructions[^1];
        if (warp.Bank != 2004 || warp.ID != 41 || warp.ArgData.Length != 16
            || BitConverter.ToUInt32(warp.ArgData, 0) != character || warp.ArgData[4] != 1
            || BitConverter.ToUInt32(warp.ArgData, 8) != region.EntityID || BitConverter.ToInt32(warp.ArgData, 12) != -1
            || region.Position != player.Position || region.Rotation != player.Rotation)
            throw new InvalidDataException("Written opening arrival does not target the terrain spawn");
        opening.Instructions.RemoveAt(opening.Instructions.Count - 1);
        if (!before.Write().AsSpan().SequenceEqual(after.Write()))
            throw new InvalidDataException("Arrival edit changed pre-existing event data");
    }

    static (EMEVD.Event Opening, uint Character, MSBE.Region.SpawnPoint Region) Bind(EMEVD events, MSBE layout)
    {
        // lazy: the inspected opening has one weather/time cutscene and one respawn assignment.
        // Revisit selection if a host update changes that structure; never select an event by ordinal.
        var opening = events.Events.Single(e => e.Instructions.Any(i => i.Bank == 2002 && i.ID == 10)
            && e.Instructions.Any(i => i.Bank == 2003 && i.ID == 23));
        var cutscene = opening.Instructions.Single(i => i.Bank == 2002 && i.ID == 10);
        var respawn = opening.Instructions.Single(i => i.Bank == 2003 && i.ID == 23);
        if (opening.Parameters.Count != 0 || cutscene.ArgData.Length != 24 || respawn.ArgData.Length != 4)
            throw new InvalidDataException("Unsupported opening event argument layout");
        uint character = BitConverter.ToUInt32(cutscene.ArgData, 8);
        uint destination = BitConverter.ToUInt32(respawn.ArgData, 0);
        if (character == 0 || destination == 0) throw new InvalidDataException("Opening arrival needs explicit entity references");
        return (opening, character, layout.Regions.SpawnPoints.Single(r => r.EntityID == destination));
    }

    /// <summary>Check arrival editing through serialization using an authored event and layout.</summary>
    public static void Check()
    {
        var events = new EMEVD(EMEVD.Game.DarkSouls3);
        var opening = new EMEVD.Event(42);
        var cutsceneArgs = new byte[24];
        BitConverter.GetBytes(1234u).CopyTo(cutsceneArgs, 8);
        opening.Instructions.Add(new EMEVD.Instruction(2002, 10, cutsceneArgs));
        opening.Instructions.Add(new EMEVD.Instruction(2003, 23, BitConverter.GetBytes(77u)));
        events.Events.Add(opening);
        var original = events.Write();
        var layout = new MSBE();
        layout.Regions.SpawnPoints.Add(new MSBE.Region.SpawnPoint { EntityID = 77, Name = "our-respawn" });
        var player = new MSBE.Part.Player { Name = "our-player", Position = new(3, 7, 11), Rotation = new(0, 90, 0) };
        layout.Parts.Players.Add(player);
        Apply(events, layout, player);
        var writtenLayout = MSBE.Read(layout.Write());
        var region = writtenLayout.Regions.SpawnPoints.Single();
        if (region.Position != player.Position || region.Rotation != player.Rotation)
            throw new InvalidDataException("Opening respawn region must follow the terrain spawn");
        var written = EMEVD.Read(events.Write()).Events.Single().Instructions;
        if (written.Count != 3 || written[0].Bank != 2002 || written[0].ID != 10
            || !written[0].ArgData.AsSpan().SequenceEqual(cutsceneArgs)
            || written[1].Bank != 2003 || written[1].ID != 23)
            throw new InvalidDataException("Arrival edit changed the existing event prefix");
        var warp = written[^1];
        if (warp.Bank != 2004 || warp.ID != 41 || warp.ArgData.Length != 16
            || BitConverter.ToUInt32(warp.ArgData, 0) != 1234 || warp.ArgData[4] != 1
            || BitConverter.ToUInt32(warp.ArgData, 8) != 77 || BitConverter.ToInt32(warp.ArgData, 12) != -1)
            throw new InvalidDataException("Opening event must finish with a warp to its relocated respawn region");
        Verify(original, events.Write(), writtenLayout, player);
        Console.WriteLine("PASS: authored opening event and respawn placement read back");
    }
}
