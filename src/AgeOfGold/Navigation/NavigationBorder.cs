using System.Numerics;

namespace AgeOfGold.Navigation;

/// <summary>
/// Moves a walkable mesh's border outward, toward the walls and drops that bound the floor it lies on. The source keeps
/// its mesh a character's half-width inside the walls; the host wants the floor itself and allows for each character's
/// width when it searches, so a source strip left as it is reads as too narrow to pass.
/// </summary>
internal static class NavigationBorder
{
    /// <param name="Moved">Border vertices that moved.</param>
    /// <param name="Furthest">The longest move, in metres.</param>
    internal sealed record Widened(Vector3[] Vertices, int Moved, float Furthest);

    // Walls are felt at knee-clearing and chest height, so stair risers and kerbs do not count as walls.
    private static readonly float[] WallHeights = [0.6f, 1.2f];
    // A border stops this far short of a wall, and floor counts as the same floor within this height of the vertex.
    private const float Skin = 0.02f, FloorBand = 0.6f;
    // Another border counts as facing this one when it lies within this height of it.
    private const float SameLevel = 1;
    // The least upward area (doubled) a face may be left with, as the reader requires of the source's own.
    private const float UprightFloor = 2e-4f;
    private const float Cell = 4;

    /// <param name="solid">The collision the mesh lies on, in the mesh's coordinates.</param>
    /// <param name="reach">How far a border edge may move out, in metres.</param>
    internal static Widened Widen(IReadOnlyList<Vector3> vertices, IReadOnlyList<(int A, int B, int C)> triangles,
        IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> solid, float reach)
    {
        if (!(reach > 0) || !float.IsFinite(reach)) throw new InvalidDataException("A border's reach must be a positive length");
        var directed = new HashSet<(int, int)>();
        float Up((int A, int B, int C) t, IReadOnlyList<Vector3> at) => Vector3.Cross(at[t.B] - at[t.A], at[t.C] - at[t.A]).Y;
        foreach (var t in triangles)
        {
            if (new[] { t.A, t.B, t.C }.Any(v => v < 0 || v >= vertices.Count)) throw new InvalidDataException("A walkable triangle names a missing vertex");
            if (!(Up(t, vertices) > 0)) throw new InvalidDataException("A walkable triangle is wound downward or stands on edge");
            directed.Add((t.A, t.B)); directed.Add((t.B, t.C)); directed.Add((t.C, t.A));
        }
        var ground = new Grid(solid);

        // A border edge has no face running the other way along it. Faces are wound with their normal up, so the floor
        // lies to one side of the edge's direction and this normal points to the other.
        var border = new List<(int A, int B, Vector3 Out)>();
        foreach (var (a, b) in directed)
        {
            if (directed.Contains((b, a))) continue;
            var along = vertices[b] - vertices[a]; var flat = new Vector3(-along.Z, 0, along.X);
            // An edge that climbs straight up has no side to move to.
            if (flat.Length() > 1e-6f) border.Add((a, b, Vector3.Normalize(flat)));
        }
        border.Sort((x, y) => x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));
        var edgesAt = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < border.Count; i++)
            foreach (int v in new[] { border[i].A, border[i].B })
            {
                if (!edgesAt.TryGetValue(v, out var list)) edgesAt[v] = list = [];
                list.Add(i);
            }

        var direction = new Dictionary<int, Vector3>(); var distance = new Dictionary<int, float>();
        foreach (var (v, edges) in edgesAt)
        {
            var sum = edges.Aggregate(Vector3.Zero, (s, e) => s + border[e].Out);
            // Borders that face opposite ways at one vertex (two fans meeting at a point) leave it nowhere to go.
            if (sum.Length() < 1e-3f) continue;
            var d = Vector3.Normalize(sum);
            float least = edges.Min(e => Vector3.Dot(d, border[e].Out));
            if (least <= 0.1f) continue;
            // Each border edge wants to move out by the reach, so a corner moves further along its bisector, to twice at most.
            float s = MathF.Min(reach / least, 2 * reach);
            var p = vertices[v];
            if (ground.Below(p) is not { } floor) continue;

            foreach (float height in WallHeights)
                if (ground.Ahead(p + new Vector3(0, height, 0), d, s + Skin) is { } wall) s = MathF.Min(s, MathF.Max(0, wall - Skin));
            // A wall may stand nearer an edge's middle than its ends: the edge's own move is held to what its middle allows.
            foreach (int e in edges)
            {
                var middle = (vertices[border[e].A] + vertices[border[e].B]) / 2; float share = Vector3.Dot(d, border[e].Out);
                foreach (float height in WallHeights)
                    if (ground.Ahead(middle + new Vector3(0, height, 0), border[e].Out, s * share + Skin) is { } wall) s = MathF.Min(s, MathF.Max(0, wall - Skin) / share);
            }
            // Another border ahead on the same level: meet it halfway at most, so two borders never cross.
            for (int e = 0; e < border.Count; e++)
            {
                var (a, b, _) = border[e];
                if (a == v || b == v || MathF.Abs((vertices[a].Y + vertices[b].Y) / 2 - p.Y) > SameLevel) continue;
                if (Crossing(p, d, vertices[a], vertices[b]) is { } gap && gap < 2 * s) s = MathF.Max(0, gap / 2 - Skin);
            }
            // Floor must continue under the move: beside a drop the border stops at the last floor found.
            if (ground.Below(p + d * s) is null)
            {
                float held = 0, lost = s;
                for (int i = 0; i < 10; i++) { float mid = (held + lost) / 2; if (ground.Below(p + d * mid) is null) lost = mid; else held = mid; }
                s = held;
            }
            if (s > 1e-4f) { direction[v] = d; distance[v] = s; }
        }

        var moved = vertices.ToArray();
        void Place(int v)
        {
            if (!distance.TryGetValue(v, out float s) || s <= 1e-4f) { moved[v] = vertices[v]; return; }
            var p = vertices[v] + direction[v] * s;
            // The border follows the floor: it keeps the height above the floor that it had where it started.
            if (ground.Below(vertices[v]) is { } before && ground.Below(p) is { } after) p.Y = vertices[v].Y + (after - before);
            moved[v] = p;
        }
        foreach (int v in distance.Keys.Order()) Place(v);
        // A face must stay upward; where moves would fold one, its vertices' moves are halved until it holds, then dropped.
        for (int round = 0; round < 12; round++)
        {
            var folded = triangles.Where(t => !(Up(t, moved) >= MathF.Min(UprightFloor, Up(t, vertices)))).SelectMany(t => new[] { t.A, t.B, t.C }).Where(distance.ContainsKey).Distinct().Order().ToArray();
            if (folded.Length == 0) break;
            foreach (int v in folded) { distance[v] = round < 8 ? distance[v] / 2 : 0; Place(v); }
        }
        if (triangles.Any(t => !(Up(t, moved) > 0))) throw new InvalidDataException("Widening a walkable border folded a face");
        var lengths = Enumerable.Range(0, moved.Length).Select(v => new Vector2(moved[v].X - vertices[v].X, moved[v].Z - vertices[v].Z).Length()).ToArray();
        return new(moved, lengths.Count(l => l > 1e-4f), lengths.DefaultIfEmpty(0).Max());
    }

    // Where a level ray from a point meets a segment, as seen from above: the distance along the ray, if it does.
    private static float? Crossing(Vector3 from, Vector3 d, Vector3 a, Vector3 b)
    {
        float ex = b.X - a.X, ez = b.Z - a.Z, det = d.Z * ex - d.X * ez;
        if (MathF.Abs(det) < 1e-9f) return null;
        float t = ((a.Z - from.Z) * ex - (a.X - from.X) * ez) / det, u = ((a.Z - from.Z) * d.X - (a.X - from.X) * d.Z) / det;
        return t > 1e-4f && u >= 0 && u <= 1 ? t : null;
    }

    /// <summary>The collision's triangles, bucketed over the ground plane so a short ray tests only those near it.</summary>
    private sealed class Grid
    {
        private readonly IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> triangles;
        private readonly Dictionary<(int, int), List<int>> cells = [];

        internal Grid(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> triangles)
        {
            this.triangles = triangles;
            for (int i = 0; i < triangles.Count; i++)
            {
                var (a, b, c) = triangles[i];
                var low = Vector3.Min(a, Vector3.Min(b, c)); var high = Vector3.Max(a, Vector3.Max(b, c));
                for (int x = At(low.X); x <= At(high.X); x++)
                    for (int z = At(low.Z); z <= At(high.Z); z++)
                    {
                        if (!cells.TryGetValue((x, z), out var list)) cells[(x, z)] = list = [];
                        list.Add(i);
                    }
            }
        }

        private static int At(float value) => (int)MathF.Floor(value / Cell);

        /// <summary>The distance to the nearest collision along a ray, within a length.</summary>
        internal float? Ahead(Vector3 origin, Vector3 d, float length)
        {
            var end = origin + d * length; float? best = null;
            var seen = new HashSet<int>();
            for (int x = At(MathF.Min(origin.X, end.X)); x <= At(MathF.Max(origin.X, end.X)); x++)
                for (int z = At(MathF.Min(origin.Z, end.Z)); z <= At(MathF.Max(origin.Z, end.Z)); z++)
                {
                    if (!cells.TryGetValue((x, z), out var list)) continue;
                    foreach (int i in list)
                    {
                        if (!seen.Add(i)) continue;
                        var (a, b, c) = triangles[i];
                        var e1 = b - a; var e2 = c - a; var h = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, h);
                        if (MathF.Abs(det) < 1e-9f) continue;
                        var s = origin - a; float u = Vector3.Dot(s, h) / det; if (u < 0 || u > 1) continue;
                        var q = Vector3.Cross(s, e1); float w = Vector3.Dot(d, q) / det; if (w < 0 || u + w > 1) continue;
                        float t = Vector3.Dot(e2, q) / det;
                        if (t >= 0 && t <= length && !(t >= best)) best = t;
                    }
                }
            return best;
        }

        /// <summary>The height of the floor under a point, if collision lies within the floor band of it.</summary>
        internal float? Below(Vector3 point) =>
            Ahead(point + new Vector3(0, FloorBand, 0), new Vector3(0, -1, 0), 2 * FloorBand) is { } fall ? point.Y + FloorBand - fall : null;
    }
}
