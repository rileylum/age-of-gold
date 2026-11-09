using System.Numerics;

namespace AgeOfGold.Collision;

internal static class TerrainGeometry
{
    internal sealed record Mesh(List<Vector3> Vertices, List<(int A, int B, int C)> Triangles, int ZeroAreaFaces);
    internal static Mesh Place(SourceCollisionReader.Body body, SourceCollisionReader.Placement placement, Vector3 offset)
    {
        if (placement.Role != "solid" || placement.Disabled)
            throw new InvalidDataException("Terrain requires enabled ordinary solid collision");
        // lazy: the accepted Firelink terrain has translation-only placements; reject other transforms until needed.
        if (Vector(placement.Rotation) != Vector3.Zero || Vector(placement.Scale) != Vector3.One)
            throw new InvalidDataException("Unsupported terrain placement transform");
        var position = Vector(placement.Position) + offset;
        var t = body.BodyTransform;
        if (t.Length != 16 || t.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid collision body transform");
        // Serialized Havok basis columns become rows for System.Numerics' row-vector multiplication.
        var transform = new Matrix4x4(t[0],t[1],t[2],t[3],t[4],t[5],t[6],t[7],t[8],t[9],t[10],t[11],t[12],t[13],t[14],t[15]);
        var vertices = new List<Vector3>();
        var triangles = new List<(int, int, int)>();
        int zeroArea = 0;
        foreach (var mesh in body.Meshes)
        {
            if (mesh.FlipAlternateTriangles != 0 || Vector(mesh.Extrusion) != Vector3.Zero)
                throw new InvalidDataException("Unsupported source winding or extrusion");
            var q = mesh.Rotation;
            var rotation = new Quaternion(q[0], q[1], q[2], q[3]);
            var scale = Vector(mesh.Scale);
            var translation = Vector(mesh.Translation);
            int first = vertices.Count;
            foreach (var vertex in mesh.Vertices)
                vertices.Add(Vector3.Transform(Vector3.Transform(Vector(vertex) * scale, rotation) + translation, transform) + position);
            foreach (var face in mesh.Faces)
            {
                int a = first + face[0], b = first + face[1], c = first + face[2];
                if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).LengthSquared() == 0)
                {
                    zeroArea++;
                    continue;
                }
                // The checked host mesh writer takes the opposite winding to source ground triangles.
                triangles.Add((a, c, b));
            }
        }
        return new(vertices, triangles, zeroArea);
    }

    internal static float GroundBelow(IEnumerable<Mesh> meshes, Vector3 point)
    {
        float ground = float.NegativeInfinity;
        foreach (var mesh in meshes)
            foreach (var (ia, ib, ic) in mesh.Triangles)
            {
                var a = mesh.Vertices[ia]; var b = mesh.Vertices[ib]; var c = mesh.Vertices[ic];
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.Y <= 0) continue;
                float u = ((point.X - a.X) * (c.Z - a.Z) - (point.Z - a.Z) * (c.X - a.X)) / -normal.Y;
                float v = ((b.X - a.X) * (point.Z - a.Z) - (b.Z - a.Z) * (point.X - a.X)) / -normal.Y;
                if (u < 0 || v < 0 || u + v > 1) continue;
                float y = a.Y + u * (b.Y - a.Y) + v * (c.Y - a.Y);
                if (y <= point.Y) ground = Math.Max(ground, y);
            }
        if (!float.IsFinite(ground)) throw new InvalidDataException("No upward-facing terrain below the requested point");
        return ground;
    }

    private static Vector3 Vector(float[] value)
    {
        var vector = new Vector3(value[0], value[1], value[2]);
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new InvalidDataException("Non-finite terrain vector");
        return vector;
    }
}
