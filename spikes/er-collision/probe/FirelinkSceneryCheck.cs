using System.Numerics;
using SoulsFormats;

static class FirelinkSceneryCheck
{
    /// <summary>Check host scenery isolation through serialized map input and output.</summary>
    public static void Run()
    {
        var native = new MSBE();
        native.Models.MapPieces.Add(new() { Name = "our-host-model" });
        native.Models.Assets.Add(new() { Name = "our-host-prop" });
        native.Parts.MapPieces.Add(new() { Name = "our-host-scenery", ModelName = "our-host-model" });
        native.Parts.Assets.Add(new() { Name = "our-host-object", ModelName = "our-host-prop" });
        native.Parts.Collisions.Add(new() { Name = "our-ground", Position = new(1, 2, 3) });
        native.Regions.EnvironmentMapPoints.Add(new() { Name = "our-light", Position = new(4, 5, 6) });
        var original = native.Write(new DCX.NoCompressionInfo());
        var map = MSBE.Read(original);
        // A translated piece may use a host model; provenance is the placement, not its model.
        map.Parts.MapPieces.Add(new() { Name = "our-translated-scenery", ModelName = "our-host-model" });
        map.Parts.MapPieces[0].Position = new(7, 8, 9);
        var input = map.Write(new DCX.NoCompressionInfo());
        var result = MSBE.Read(FirelinkScenery.Isolate(original, input));
        foreach (var part in result.Parts.GetEntries().Where(p => p.Name is "our-host-scenery" or "our-host-object"))
        {
            if (part.GameEditionDisable != MSBE.Part.GameEditionDisableType.DisableInRelease)
                throw new InvalidDataException("Retained host scenery must be disabled");
            part.GameEditionDisable = MSBE.Part.GameEditionDisableType.NeverDisable;
        }
        if (!result.Write(new DCX.NoCompressionInfo()).AsSpan().SequenceEqual(input))
            throw new InvalidDataException("Translated scenery, collision or other map data changed");
        Console.WriteLine("PASS: retained host scenery disabled; translated scenery and unrelated map bytes preserved");
    }
}
