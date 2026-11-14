using System.Numerics;
using AgeOfGold.Navigation;

internal static class NavigationBorderCheck
{
    internal static void Run()
    {
        const float reach = 0.5f;
        var solid = new List<(Vector3, Vector3, Vector3)>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { solid.Add((a, b, c)); solid.Add((a, c, d)); }
        void Floor(float x0, float x1, float z0, float z1, float y) => Quad(new(x0, y, z0), new(x0, y, z1), new(x1, y, z1), new(x1, y, z0));
        void WallAcross(float x, float z0, float z1, float y0, float y1) => Quad(new(x, y0, z0), new(x, y1, z0), new(x, y1, z1), new(x, y0, z1));

        // A strip of walkable floor: two columns of vertices at x0 and x1, joined by upward-wound triangles.
        static (Vector3[] Vertices, (int, int, int)[] Triangles) Strip(float x0, float x1, float y, params float[] z)
        {
            var vertices = z.SelectMany(at => new[] { new Vector3(x0, y, at), new Vector3(x1, y, at) }).ToArray();
            var triangles = new List<(int, int, int)>();
            for (int i = 0; i + 1 < z.Length; i++) { triangles.Add((2 * i, 2 * i + 2, 2 * i + 3)); triangles.Add((2 * i, 2 * i + 3, 2 * i + 1)); }
            return (vertices, triangles.ToArray());
        }
        static void Upward(Vector3[] vertices, (int A, int B, int C)[] triangles)
        {
            foreach (var t in triangles)
                if (!(Vector3.Cross(vertices[t.B] - vertices[t.A], vertices[t.C] - vertices[t.A]).Y > 0)) throw new Exception("A triangle no longer faces up");
        }

        // A corridor: floor two metres wide between walls at x = -1 and x = 1, the source's strip 0.8 m wide down its middle.
        Floor(-1, 1, -20, 30, 0); WallAcross(-1, -20, 30, 0, 3); WallAcross(1, -20, 30, 0, 3);
        var (corridor, corridorFaces) = Strip(-0.4f, 0.4f, 0, 0, 5, 10);
        var widened = NavigationBorder.Widen(corridor, corridorFaces, solid, reach);
        if (widened.Vertices.Length != corridor.Length) throw new Exception("Widening changed the vertex count");
        Upward(widened.Vertices, corridorFaces);
        // The walls stand 0.6 m from the strip, further than the reach: the sides move out by the reach and stop short of the walls.
        for (int i = 2; i < 4; i++)
            if (MathF.Abs(MathF.Abs(widened.Vertices[i].X) - 0.9f) > 1e-3f || widened.Vertices[i].Z != corridor[i].Z || MathF.Abs(widened.Vertices[i].Y) > 1e-5f)
                throw new Exception($"A side vertex with room to spare moved to {widened.Vertices[i]}");
        // The ends are open floor: an end corner moves out along both of its borders.
        if (MathF.Abs(widened.Vertices[0].X + 0.9f) > 1e-3f || MathF.Abs(widened.Vertices[0].Z + 0.5f) > 1e-3f) throw new Exception($"An end corner moved to {widened.Vertices[0]}");
        if (widened.Moved != 6 || MathF.Abs(widened.Furthest - reach * MathF.Sqrt(2)) > 1e-3f) throw new Exception($"Widening reported {widened.Moved} moved, furthest {widened.Furthest}");

        // With a longer reach the sides stop at the walls instead of passing through them.
        var stopped = NavigationBorder.Widen(corridor, corridorFaces, solid, 1.5f);
        for (int i = 2; i < 4; i++)
            if (MathF.Abs(stopped.Vertices[i].X) is < 0.95f or > 1.0f) throw new Exception($"A side vertex was not stopped by its wall: {stopped.Vertices[i]}");
        Upward(stopped.Vertices, corridorFaces);

        // A ledge: the floor ends 0.2 m beyond the strip's right side, with no wall. The border stays over the floor.
        solid.Clear(); Floor(-10, 0.6f, -20, 30, 0);
        var ledge = NavigationBorder.Widen(corridor, corridorFaces, solid, reach);
        if (ledge.Vertices[3].X is < 0.5f or > 0.6f) throw new Exception($"A vertex beside a drop moved to {ledge.Vertices[3]}");
        if (MathF.Abs(ledge.Vertices[2].X + 0.9f) > 1e-3f) throw new Exception("A vertex on open floor did not move by the reach");

        // Two strips 0.4 m apart on open floor: their facing borders meet in the middle at most, never crossing.
        solid.Clear(); Floor(-10, 10, -20, 30, 0);
        var (left, leftFaces) = Strip(-1.2f, -0.2f, 0, 0, 5, 10); var (right, rightFaces) = Strip(0.2f, 1.2f, 0, 0, 5, 10);
        var both = left.Concat(right).ToArray();
        var bothFaces = leftFaces.Concat(rightFaces.Select(t => (t.Item1 + left.Length, t.Item2 + left.Length, t.Item3 + left.Length))).ToArray();
        var apart = NavigationBorder.Widen(both, bothFaces, solid, reach);
        Upward(apart.Vertices, bothFaces);
        if (!(apart.Vertices[3].X > -0.2f) || !(apart.Vertices[3].X <= 0) || !(apart.Vertices[left.Length + 2].X < 0.2f) || !(apart.Vertices[left.Length + 2].X >= 0))
            throw new Exception($"Facing borders crossed or did not move: {apart.Vertices[3]} and {apart.Vertices[left.Length + 2]}");

        // An inner vertex stays where it is: a strip of three columns moves only its outer two.
        var grid = new Vector3[] { new(-1, 0, 0), new(0, 0, 0), new(1, 0, 0), new(-1, 0, 4), new(0, 0, 4), new(1, 0, 4), new(-1, 0, 8), new(0, 0, 8), new(1, 0, 8) };
        var gridFaces = new (int, int, int)[] { (0, 3, 4), (0, 4, 1), (1, 4, 5), (1, 5, 2), (3, 6, 7), (3, 7, 4), (4, 7, 8), (4, 8, 5) };
        var inner = NavigationBorder.Widen(grid, gridFaces, solid, reach);
        if (inner.Vertices[4] != grid[4] || inner.Moved != 8) throw new Exception("An inner vertex moved, or a border vertex did not");

        // A stair: floor rising in 0.25 m steps along the strip. Risers are not walls; the border still widens sideways.
        solid.Clear();
        for (int step = 0; step < 8; step++) { Floor(-2, 2, step, step + 1, step * 0.25f); Quad(new(-2, step * 0.25f, step + 1), new(-2, step * 0.25f + 0.25f, step + 1), new(2, step * 0.25f + 0.25f, step + 1), new(2, step * 0.25f, step + 1)); }
        var stair = new Vector3[] { new(-0.4f, 0.5f, 2.5f), new(0.4f, 0.5f, 2.5f), new(-0.4f, 1.25f, 5.5f), new(0.4f, 1.25f, 5.5f) };
        var stairFaces = new (int, int, int)[] { (0, 2, 3), (0, 3, 1) };
        var climbed = NavigationBorder.Widen(stair, stairFaces, solid, reach);
        if (!(climbed.Vertices[0].X < -0.75f) || !(climbed.Vertices[3].X > 0.75f)) throw new Exception($"Stair borders did not widen sideways: {climbed.Vertices[0]} {climbed.Vertices[3]}");
        Upward(climbed.Vertices, stairFaces);

        foreach (var bad in new Action[]
        {
            () => NavigationBorder.Widen(corridor, corridorFaces, solid, 0),
            () => NavigationBorder.Widen(corridor, [(0, 1, 9)], solid, reach),
            () => NavigationBorder.Widen(corridor, [(0, 1, 2)], solid, reach),
        })
        {
            try { bad(); throw new Exception("Invalid widening input was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a walkable border widens by its reach, stops at walls, drops and facing borders, and keeps every face upward");
    }
}
