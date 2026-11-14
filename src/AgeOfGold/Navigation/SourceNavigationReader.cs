using System.Numerics;
using SoulsFormats;

namespace AgeOfGold.Navigation;

/// <summary>Reads the source map's navigation triangles into one mesh, as the host wants them: welded, walkable and wound upward.</summary>
internal static class SourceNavigationReader
{
    /// <param name="Dropped">Source triangles left out, by reason.</param>
    internal sealed record Mesh(Vector3[] Vertices, (int A, int B, int C)[] Triangles, SortedDictionary<string, int> Dropped);

    // Triangles carrying any of these source marks are not open floor.
    // lazy: marks are only used to leave triangles out; doors, gates, ladders and events need their own translations.
    private const NVM.TriangleFlags Closed = NVM.TriangleFlags.INSIDE_WALL | NVM.TriangleFlags.BLOCK_GATE | NVM.TriangleFlags.CLOSED_DOOR | NVM.TriangleFlags.LADDER
        | NVM.TriangleFlags.FLOOR_TO_WALL | NVM.TriangleFlags.DEGENERATE | NVM.TriangleFlags.WALL | NVM.TriangleFlags.BLOCK | NVM.TriangleFlags.DISABLE;

    private const float UprightFloor = 2e-4f;

    internal static Mesh Read(Install source)
    {
        var layout = MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb"));
        var files = BND3.Read(source.Read("map/m10_02_00_00/m10_02_00_00.nvmbnd.dcx")).Files
            .ToDictionary(f => f.Name.Replace('\\', '/').Split('/')[^1], f => f.Bytes, StringComparer.OrdinalIgnoreCase);
        var vertices = new List<Vector3>(); var welded = new Dictionary<(int, int, int), int>();
        var triangles = new List<(int, int, int)>(); var directed = new HashSet<(int, int)>();
        var dropped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Drop(string reason) => dropped[reason] = dropped.GetValueOrDefault(reason) + 1;
        foreach (var part in layout.Parts.Navmeshes.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            // Placements carry rotations of a few millionths of a degree: authoring noise, not a turn.
            if (part.Position != Vector3.Zero || part.Rotation.Length() > 1e-3f || part.Scale != Vector3.One) throw new InvalidDataException("Source navigation mesh is moved by its placement: " + part.Name);
            if (!files.TryGetValue(part.ModelName + "A10.nvm", out var bytes)) throw new InvalidDataException("Source navigation mesh file is missing: " + part.ModelName);
            var mesh = NVM.Read(bytes);
            foreach (var triangle in mesh.Triangles)
            {
                if ((triangle.Flags & Closed) != 0) { Drop("closed: " + (triangle.Flags & Closed)); continue; }
                int[] corners = [triangle.VertexIndex1, triangle.VertexIndex2, triangle.VertexIndex3];
                if (corners.Any(c => c < 0 || c >= mesh.Vertices.Count)) throw new InvalidDataException("Source navigation triangle names a missing vertex: " + part.ModelName);
                var points = corners.Select(c => mesh.Vertices[c]).ToArray();
                // Meshes meet along shared positions; welding to the millimetre joins them into one walkable surface.
                int Weld(Vector3 p)
                {
                    var key = ((int)MathF.Round(p.X * 1000), (int)MathF.Round(p.Y * 1000), (int)MathF.Round(p.Z * 1000));
                    if (!welded.TryGetValue(key, out int index)) { welded.Add(key, index = vertices.Count); vertices.Add(p); }
                    return index;
                }
                var (a, b, c) = (Weld(points[0]), Weld(points[1]), Weld(points[2]));
                if (a == b || b == c || c == a) { Drop("collapsed by welding"); continue; }
                // The host walks on the side a face's normal points to, so every face is wound with its normal upward. A face
                // with under a square centimetre of floor (twice that in this cross product) stands on edge and is left out.
                points = [vertices[a], vertices[b], vertices[c]];
                float up = Vector3.Cross(points[1] - points[0], points[2] - points[0]).Y;
                if (MathF.Abs(up) < UprightFloor) { Drop("on edge"); continue; }
                if (up < 0) { (b, c) = (c, b); (points[1], points[2]) = (points[2], points[1]); }
                if (directed.Contains((a, b)) || directed.Contains((b, c)) || directed.Contains((c, a)))
                {
                    // A second face running the same way along an edge cannot share it; it keeps its own vertices and joins nothing.
                    Drop("joined to nothing: overlapping edge");
                    a = vertices.Count; b = a + 1; c = a + 2; vertices.AddRange(points);
                }
                directed.Add((a, b)); directed.Add((b, c)); directed.Add((c, a));
                triangles.Add((a, b, c));
            }
        }
        if (triangles.Count == 0) throw new InvalidDataException("Source map has no walkable navigation triangles");
        return new(vertices.ToArray(), triangles.ToArray(), dropped);
    }
}
