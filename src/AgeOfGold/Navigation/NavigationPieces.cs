using System.Numerics;

namespace AgeOfGold.Navigation;

/// <summary>A walkable mesh divided into the pieces a game loads one by one, with where the pieces meet.</summary>
internal static class NavigationPieces
{
    /// <param name="Part">The source part the piece's triangles came from.</param>
    internal sealed record Piece(string Part, Vector3[] Vertices, (int A, int B, int C)[] Triangles, Gate[] Gates);

    /// <summary>The border edges along which one piece continues into another: a face and edge on each side, three edges to a face.</summary>
    internal sealed record Join(int From, int To, (int Face, int Edge, int OtherFace, int OtherEdge)[] Edges);

    /// <summary>One unbroken run of joined edges, seen from one of its two pieces.</summary>
    /// <param name="Other">The neighbouring piece.</param>
    /// <param name="Counterpart">The same gate's place among the neighbour's gates.</param>
    /// <param name="Distances">The walk, in metres, to each gate of the same piece; null where there is none.</param>
    internal sealed record Gate(Vector3 Position, int Other, int Counterpart, float?[] Distances);

    internal sealed record Division(Piece[] Pieces, Join[] Joins);

    /// <param name="parts">The source part of each triangle; one piece is made per part, in name order.</param>
    internal static Division Split(IReadOnlyList<Vector3> vertices, IReadOnlyList<(int A, int B, int C)> triangles, IReadOnlyList<string> parts)
    {
        if (parts.Count != triangles.Count || parts.Any(string.IsNullOrEmpty)) throw new InvalidDataException("Every walkable triangle needs its source part");
        var names = parts.Distinct().Order(StringComparer.Ordinal).ToArray();
        var pieceOf = names.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i, StringComparer.Ordinal);
        var points = names.Select(_ => new List<Vector3>()).ToArray(); var faces = names.Select(_ => new List<(int A, int B, int C)>()).ToArray();
        var local = names.Select(_ => new Dictionary<int, int>()).ToArray();
        // Where each directed edge of the whole mesh lies: its piece, and its edge number there.
        var places = new Dictionary<(int, int), (int Piece, int Edge)>();
        for (int t = 0; t < triangles.Count; t++)
        {
            int piece = pieceOf[parts[t]]; var (a, b, c) = triangles[t];
            if (new[] { a, b, c }.Any(v => v < 0 || v >= vertices.Count)) throw new InvalidDataException("A walkable triangle names a missing vertex");
            int Local(int v)
            {
                if (!local[piece].TryGetValue(v, out int index)) { local[piece].Add(v, index = points[piece].Count); points[piece].Add(vertices[v]); }
                return index;
            }
            int face = faces[piece].Count;
            faces[piece].Add((Local(a), Local(b), Local(c)));
            foreach (var (corner, from, to) in new[] { (0, a, b), (1, b, c), (2, c, a) })
                if (!places.TryAdd((from, to), (piece, face * 3 + corner))) throw new InvalidDataException("Two walkable faces run the same way along one edge");
        }

        // A joined edge has its twin, running the other way, in another piece. Each pair is found once, from its lower piece.
        var shared = new SortedDictionary<(int From, int To), List<(int Edge, int OtherEdge, int A, int B)>>();
        foreach (var ((a, b), (piece, edge)) in places)
            if (places.TryGetValue((b, a), out var twin) && twin.Piece > piece)
            {
                if (!shared.TryGetValue((piece, twin.Piece), out var list)) shared[(piece, twin.Piece)] = list = [];
                list.Add((edge, twin.Edge, a, b));
            }
        var joins = new SortedDictionary<(int, int), Join>();
        var gates = names.Select(_ => new List<(Vector3 Position, int Other, int Face, int Run)>()).ToArray();
        var counterpart = new Dictionary<(int Piece, int Other, int Run), int>();
        foreach (var ((from, to), edges) in shared)
        {
            edges.Sort((x, y) => x.Edge.CompareTo(y.Edge));
            joins[(from, to)] = new(from, to, edges.Select(e => (e.Edge / 3, e.Edge, e.OtherEdge / 3, e.OtherEdge)).ToArray());
            joins[(to, from)] = new(to, from, edges.Select(e => (e.OtherEdge / 3, e.OtherEdge, e.Edge / 3, e.Edge)).ToArray());
            // Edges that share a vertex belong to one run: one gate, standing at the middle of its longest edge.
            var run = Enumerable.Range(0, edges.Count).ToArray();
            int Find(int x) { while (run[x] != x) x = run[x]; return x; }
            var at = new Dictionary<int, int>();
            for (int i = 0; i < edges.Count; i++)
                foreach (int v in new[] { edges[i].A, edges[i].B })
                    if (at.TryGetValue(v, out int other)) run[Find(i)] = Find(other); else at[v] = i;
            int number = 0;
            foreach (var group in Enumerable.Range(0, edges.Count).GroupBy(Find).OrderBy(g => g.Min()))
            {
                int longest = group.OrderByDescending(i => (vertices[edges[i].A] - vertices[edges[i].B]).Length()).ThenBy(i => i).First();
                var position = (vertices[edges[longest].A] + vertices[edges[longest].B]) / 2;
                gates[from].Add((position, to, edges[longest].Edge / 3, number)); gates[to].Add((position, from, edges[longest].OtherEdge / 3, number));
                number++;
            }
        }
        // A piece lists its gates by neighbour, then by run; a gate's counterpart is the same run in the neighbour's list.
        for (int piece = 0; piece < names.Length; piece++)
        {
            gates[piece].Sort((x, y) => x.Other != y.Other ? x.Other.CompareTo(y.Other) : x.Run.CompareTo(y.Run));
            for (int g = 0; g < gates[piece].Count; g++) counterpart[(piece, gates[piece][g].Other, gates[piece][g].Run)] = g;
        }
        var pieces = new Piece[names.Length];
        for (int piece = 0; piece < names.Length; piece++)
        {
            var mine = gates[piece]; var distances = mine.Select(_ => new float?[mine.Count]).ToArray();
            var walk = new Walk(points[piece], faces[piece]);
            for (int g = 0; g < mine.Count; g++)
            {
                var reached = walk.From(mine[g].Face);
                for (int h = g; h < mine.Count; h++)
                {
                    float? distance = h == g ? 0 : mine[h].Face == mine[g].Face ? (mine[g].Position - mine[h].Position).Length()
                        : reached.TryGetValue(mine[h].Face, out float along) ? (mine[g].Position - walk.Centre(mine[g].Face)).Length() + along + (walk.Centre(mine[h].Face) - mine[h].Position).Length() : null;
                    distances[g][h] = distances[h][g] = distance;
                }
            }
            pieces[piece] = new(names[piece], points[piece].ToArray(), faces[piece].ToArray(),
                mine.Select((gate, g) => new Gate(gate.Position, gate.Other, counterpart[(gate.Other, piece, gate.Run)], distances[g])).ToArray());
        }
        return new(pieces, joins.Values.ToArray());
    }

    // lazy: a walk is measured from face centre to face centre, which overstates a straight crossing by up to a third;
    // the distances only rank routes between gates. Measure true shortest paths if route choice between pieces looks wrong.
    private sealed class Walk
    {
        private readonly Vector3[] centres;
        private readonly List<int>[] next;

        internal Walk(IReadOnlyList<Vector3> vertices, IReadOnlyList<(int A, int B, int C)> triangles)
        {
            centres = triangles.Select(t => (vertices[t.A] + vertices[t.B] + vertices[t.C]) / 3).ToArray();
            next = triangles.Select(_ => new List<int>()).ToArray();
            var starts = new Dictionary<(int, int), int>();
            for (int f = 0; f < triangles.Count; f++) { var (a, b, c) = triangles[f]; starts[(a, b)] = f; starts[(b, c)] = f; starts[(c, a)] = f; }
            foreach (var ((a, b), f) in starts) if (starts.TryGetValue((b, a), out int g)) next[f].Add(g);
            foreach (var list in next) list.Sort();
        }

        internal Vector3 Centre(int face) => centres[face];

        internal Dictionary<int, float> From(int face)
        {
            var reached = new Dictionary<int, float> { [face] = 0 }; var open = new PriorityQueue<int, (float, int)>(); open.Enqueue(face, (0, face));
            while (open.TryDequeue(out int f, out var priority))
            {
                if (priority.Item1 > reached[f]) continue;
                foreach (int g in next[f])
                {
                    float d = reached[f] + (centres[g] - centres[f]).Length();
                    if (!reached.TryGetValue(g, out float known) || d < known) { reached[g] = d; open.Enqueue(g, (d, g)); }
                }
            }
            return reached;
        }
    }
}
