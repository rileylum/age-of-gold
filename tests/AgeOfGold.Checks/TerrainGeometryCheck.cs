using System.Numerics;
using AgeOfGold.Collision;

internal static class TerrainGeometryCheck
{
    internal static void Run()
    {
        var body = new SourceCollisionReader.Body("authored", [0,0,-1,0,0,1,0,0,1,0,0,0,5,6,7,1], [
            new([[0,0,0,0],[2,0,0,0],[0,0,3,0]], [[0,1,2,0],[0,0,1,0]], 16, 23,
                [0,0,0,0], [0,0,0,1], [1,1,1,0], 0, [0,0,0,0]),
        ]);
        var placement = new SourceCollisionReader.Placement("authored", "high", "low", "solid", false,
            [10,20,30], [0,0,0], [1,1,1], [1,0,0,0]);
        var mesh = TerrainGeometry.Place(body, placement, new(100,200,300));
        if (!mesh.Vertices.SequenceEqual(new Vector3[] { new(115,226,337), new(115,226,335), new(118,226,337) })
            || !mesh.Triangles.SequenceEqual(new[] { (0,2,1) }) || mesh.ZeroAreaFaces != 1)
            throw new Exception("Terrain transform, winding or degenerate-face handling differs");
        if (TerrainGeometry.GroundBelow([mesh], new(116,227,336.5f)) != 226)
            throw new Exception("Translated terrain does not support the expected point");
        Reject(() => TerrainGeometry.GroundBelow([mesh], new(116,225,336.5f)), "unsupported point");
        Reject(() => TerrainGeometry.Place(body, placement with { Role = "water" }, Vector3.Zero), "special role");
        Reject(() => TerrainGeometry.Place(body, placement with { Disabled = true }, Vector3.Zero), "disabled placement");
        Reject(() => TerrainGeometry.Place(body, placement with { Rotation = [0,90,0] }, Vector3.Zero), "unsupported placement transform");
        Console.WriteLine("PASS: terrain placement transforms, winding and supported ground; special roles remain separate");
    }

    private static void Reject(Action action, string description)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Accepted " + description);
    }
}
