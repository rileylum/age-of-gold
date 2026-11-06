using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using SoulsFormats;

// Spike boundary: source identifiers and placements are read from the install, never embedded.
static class FirelinkSource
{
    /// <summary>Export every Firelink collision placement and its respawn placement for the diagnostic build.</summary>
    public static void Export(string source, string output)
    {
        var path = Path.Combine(source, "map", "MapStudio", "m10_02_00_00.msb");
        var bytes = File.ReadAllBytes(path);
        var layout = MSB1.Read(bytes);
        var spawnEvent = layout.Events.SpawnPoints.Single();
        var spawn = layout.Regions.GetEntries().Single(p => p.Name == spawnEvent.SpawnPointName);
        float[] V(Vector3 v) => [v.X, v.Y, v.Z];
        var parts = layout.Parts.Collisions.Select(p => new
        {
            name = p.Name, model = layout.Models.Collisions.Single(m => m.Name == p.ModelName).Name,
            position = V(p.Position), rotation = V(p.Rotation), scale = V(p.Scale),
            role = Role(p.HitFilterID), disabled = p.DisableStart,
        }).ToArray();
        var result = new
        {
            source_map = "m10_02_00_00", layout_sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            spawn = new { position = V(spawn.Position), rotation = V(spawn.Rotation) }, parts,
        };
        using var stream = new FileStream(output, FileMode.CreateNew);
        JsonSerializer.Serialize(stream, result);
        Console.WriteLine($"PASS: {parts.Length} classified collision placements, one respawn placement");
    }

    // These are source roles; their numbers must not be treated as equivalent host hit filters.
    static string Role(byte filter) => filter switch
    {
        8 => "solid", 9 => "water", 11 => "npc-only", 13 => "death-camera", 14 => "lethal-fall",
        15 => "kill-plane", 17 => "group-switch", 20 or 23 => "map-exit",
        _ => throw new InvalidDataException($"Unclassified source collision filter {filter}"),
    };
}
