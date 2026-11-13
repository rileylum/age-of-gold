using System.Numerics;
using AgeOfGold.Navigation;

internal static class NavigationTreeCheck
{
    internal static void Run()
    {
        // Authored boxes scattered on a slope, with one far outlier and two that coincide.
        var boxes = new List<(Vector3 Min, Vector3 Max)>();
        for (int i = 0; i < 37; i++)
        {
            var at = new Vector3(i * 1.7f % 11, i * 0.13f, i * 2.9f % 7);
            boxes.Add((at, at + new Vector3(0.5f + i % 3, 0.05f, 0.4f + i % 2)));
        }
        boxes.Add((new(90, 40, -60), new(91, 41, -59)));
        boxes.Add(boxes[5]);
        var tree = BoundingTree.Build(boxes);
        if (!tree.Nodes.SequenceEqual(BoundingTree.Build(boxes).Nodes)) throw new Exception("Bounding tree does not repeat");
        if (tree.Nodes.Length != 6 * (2 * boxes.Count - 1)) throw new Exception("A tree over n boxes needs 2n - 1 six-byte nodes");
        if (tree.Min != boxes.Select(b => b.Min).Aggregate(Vector3.Min) || tree.Max != boxes.Select(b => b.Max).Aggregate(Vector3.Max)) throw new Exception("Tree domain is not the boxes' bounds");
        // Decode as the host does: each axis byte holds two four-bit steps whose squares over 226 pull the parent's box in;
        // the fourth byte's top bit marks an inner node, whose value counts the leaves of its first child (which follows it);
        // a leaf's value is the box it stands for.
        var seen = new int[boxes.Count]; int visited = 0;
        void Walk(int index, Vector3 min, Vector3 max, int depth)
        {
            if (depth > 64 || index < 0 || index * 6 + 6 > tree.Nodes.Length) throw new Exception("Tree walk left the node table");
            visited++;
            var node = tree.Nodes.AsSpan(index * 6, 6);
            float[] low = [min.X, min.Y, min.Z], high = [max.X, max.Y, max.Z];
            for (int axis = 0; axis < 3; axis++)
            {
                float extent = high[axis] - low[axis]; int a = node[axis] >> 4, b = node[axis] & 15;
                low[axis] += a * a / 226f * extent; high[axis] -= b * b / 226f * extent;
            }
            Vector3 innerMin = new(low[0], low[1], low[2]), innerMax = new(high[0], high[1], high[2]);
            int value = (node[3] & 0x7F) << 16 | node[4] | node[5] << 8;
            if ((node[3] & 0x80) != 0)
            {
                if (value < 1) throw new Exception("An inner node has no first child leaves");
                Walk(index + 1, innerMin, innerMax, depth + 1); Walk(index + 2 * value, innerMin, innerMax, depth + 1);
                return;
            }
            if (value >= boxes.Count) throw new Exception("A leaf names a box beyond the input");
            seen[value]++;
            var outside = Vector3.Max(innerMin - boxes[value].Min, boxes[value].Max - innerMax);
            if (MathF.Max(outside.X, MathF.Max(outside.Y, outside.Z)) > 1e-4f) throw new Exception($"Leaf box does not contain box {value}: outside by {outside}");
        }
        Walk(0, tree.Min, tree.Max, 0);
        if (visited != 2 * boxes.Count - 1 || seen.Any(count => count != 1)) throw new Exception("Tree does not reach every box exactly once");
        // The root is the whole domain; one box alone is a single leaf.
        if (tree.Nodes[0] != 0 || tree.Nodes[1] != 0 || tree.Nodes[2] != 0 || (tree.Nodes[3] & 0x80) == 0) throw new Exception("Root node is not the undiminished domain");
        var single = BoundingTree.Build([boxes[0]]);
        if (!single.Nodes.SequenceEqual(new byte[6]) || single.Min != boxes[0].Min) throw new Exception("A single box is not one leaf");
        foreach (var bad in new[] { new List<(Vector3, Vector3)>(), [(new Vector3(1, 0, 0), new Vector3(0, 1, 1))], [(Vector3.Zero, new Vector3(float.NaN, 1, 1))] })
        {
            try { BoundingTree.Build(bad); }
            catch (InvalidDataException) { continue; }
            throw new Exception("Invalid boxes were accepted");
        }
        Console.WriteLine("PASS: bounding tree nodes decode to boxes containing every input exactly once, in the host's six-byte form");
    }
}
