using System.Numerics;
using SoulsFormats;

static class FirelinkEnvironmentCheck
{
    /// <summary>Check room-volume restoration without changing outdoor light or unrelated map data.</summary>
    public static void Run()
    {
        var native = new MSBE();
        native.Regions.EnvironmentMapEffectBoxes.Add(new() {
            Name = "Outside", Position = new(-2, -20, 3),
            Shape = new MSB.Shape.Box { Width = 100, Height = 100, Depth = 100 },
            PointLightMult = 0.75f,
        });
        native.Regions.EnvironmentMapEffectBoxes.Add(new() {
            Name = "Room", Position = new(3, 5, -4),
            Shape = new MSB.Shape.Box { Width = 8, Height = 12, Depth = 6 },
            Rotation = new(0, 35, 0), PointLightMult = 0.4f,
        });
        native.Regions.EnvironmentMapPoints.Add(new() { Name = "Probe", Position = new(1, 2, 3) });
        var nativeBytes = native.Write(new DCX.NoCompressionInfo());
        var moved = MSBE.Read(nativeBytes);
        foreach (var box in moved.Regions.EnvironmentMapEffectBoxes)
        {
            box.Position += new Vector3(0, 200, 0);
            box.PointLightMult *= 2;
        }
        moved.Regions.EnvironmentMapPoints[0].Position = new(4, 5, 6);
        var input = moved.Write(new DCX.NoCompressionInfo());
        var result = MSBE.Read(FirelinkEnvironment.RestoreRooms(nativeBytes, input));
        var room = result.Regions.EnvironmentMapEffectBoxes.Single(b => b.Name == "Room");
        if (room.Position != new Vector3(3, 5, -4))
            throw new InvalidDataException("The moved room must return to its native position");
        room.Position = new(3, 205, -4);
        if (!result.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(input))
            throw new InvalidDataException("Outdoor lighting or unrelated map data changed");
        Console.WriteLine("PASS: room restored; outdoor light, strengths, points and other map bytes preserved");
    }
}
