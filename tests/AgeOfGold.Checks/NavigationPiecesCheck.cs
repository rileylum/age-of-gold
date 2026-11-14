using System.Numerics;
using AgeOfGold.Navigation;

internal static class NavigationPiecesCheck
{
    internal static void Run()
    {
        // Three unit squares in a row along x, each of two upward triangles. The outer two belong to one source part and
        // the middle one to another, so the first part meets the second in two separate places.
        var vertices = Enumerable.Range(0, 4).SelectMany(i => new[] { new Vector3(i, 0, 0), new Vector3(i, 0, 1) }).ToArray();
        var triangles = Enumerable.Range(0, 3).SelectMany(i => new[] { (2 * i, 2 * i + 1, 2 * i + 3), (2 * i, 2 * i + 3, 2 * i + 2) }).ToArray();
        string[] parts = ["n0001", "n0001", "n0002", "n0002", "n0001", "n0001"];
        var split = NavigationPieces.Split(vertices, triangles, parts);
        if (split.Pieces.Length != 2 || split.Pieces[0].Part != "n0001" || split.Pieces[1].Part != "n0002") throw new Exception("Pieces are not one per part, in name order");
        var (outer, middle) = (split.Pieces[0], split.Pieces[1]);
        if (outer.Triangles.Length != 4 || outer.Vertices.Length != 8 || middle.Triangles.Length != 2 || middle.Vertices.Length != 4) throw new Exception("Pieces do not hold their own triangles and vertices");
        // A piece keeps its triangles' corners and winding in its own vertex numbering.
        Vector3[] Corners(NavigationPieces.Piece piece, int face) => [piece.Vertices[piece.Triangles[face].A], piece.Vertices[piece.Triangles[face].B], piece.Vertices[piece.Triangles[face].C]];
        if (!Corners(outer, 2).SequenceEqual(new Vector3[] { new(2, 0, 0), new(2, 0, 1), new(3, 0, 1) }) || !Corners(middle, 1).SequenceEqual(new Vector3[] { new(1, 0, 0), new(2, 0, 1), new(2, 0, 0) }))
            throw new Exception("A piece's triangle changed");

        // Joins run both ways and mirror each other: a face and edge on each side, an edge numbered three to a face.
        if (split.Joins.Length != 2 || split.Joins[0] is not { From: 0, To: 1 } || split.Joins[1] is not { From: 1, To: 0 }) throw new Exception("Joins are not listed both ways in piece order");
        if (!split.Joins[0].Edges.SequenceEqual([(1, 4, 0, 0), (2, 6, 1, 4)]) || !split.Joins[1].Edges.SequenceEqual([(0, 0, 1, 4), (1, 4, 2, 6)]))
            throw new Exception("Join edges differ: " + string.Join(" ", split.Joins[0].Edges) + " / " + string.Join(" ", split.Joins[1].Edges));
        // Each joined pair of edges runs between the same two points, in opposite directions.
        foreach (var join in split.Joins)
            foreach (var (face, edge, otherFace, otherEdge) in join.Edges)
            {
                (Vector3, Vector3) Ends(NavigationPieces.Piece piece, int f, int e) { var c = Corners(piece, f); return (c[e % 3], c[(e + 1) % 3]); }
                if (edge / 3 != face || otherEdge / 3 != otherFace) throw new Exception("A join edge does not belong to its face");
                var (a, b) = Ends(split.Pieces[join.From], face, edge); var (c, d) = Ends(split.Pieces[join.To], otherFace, otherEdge);
                if (a != d || b != c) throw new Exception("Joined edges do not coincide");
            }

        // Gates: one for each unbroken run of joined edges. The outer piece's two gates cannot reach each other inside it;
        // the middle piece's two are a short walk apart. A gate names its neighbour and its own place in that neighbour's list.
        if (outer.Gates.Length != 2 || middle.Gates.Length != 2) throw new Exception("Gates are not one per meeting place");
        for (int g = 0; g < 2; g++)
        {
            var expected = new Vector3(1 + g, 0, 0.5f);
            if (outer.Gates[g].Position != expected || middle.Gates[g].Position != expected) throw new Exception("A gate does not stand on its joined edge");
            if (outer.Gates[g] is not { Other: 1 } || outer.Gates[g].Counterpart != g || middle.Gates[g] is not { Other: 0 } || middle.Gates[g].Counterpart != g) throw new Exception("A gate does not name its counterpart");
            if (outer.Gates[g].Distances.Length != 2 || outer.Gates[g].Distances[g] != 0 || outer.Gates[g].Distances[1 - g] is not null) throw new Exception("Gates of separate floors are given a walking distance");
            if (middle.Gates[g].Distances[g] != 0 || middle.Gates[g].Distances[1 - g] is not (>= 1 and <= 1.3f)) throw new Exception("Walking distance between a piece's gates is off: " + middle.Gates[g].Distances[1 - g]);
        }
        if (middle.Gates[0].Distances[1] != middle.Gates[1].Distances[0]) throw new Exception("Walking distances are not the same both ways");

        // One part alone has no joins and no gates.
        var alone = NavigationPieces.Split(vertices, triangles, Enumerable.Repeat("n0009", 6).ToArray());
        if (alone.Pieces.Length != 1 || alone.Joins.Length != 0 || alone.Pieces[0].Gates.Length != 0 || alone.Pieces[0].Triangles.Length != 6 || alone.Pieces[0].Vertices.Length != 8) throw new Exception("A single part did not stay whole");
        try { NavigationPieces.Split(vertices, triangles, parts[..5]); throw new Exception("Triangles without a part were accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: a walkable mesh divides into one piece per part, with mirrored joins and gates that know their counterparts and distances");
    }
}
