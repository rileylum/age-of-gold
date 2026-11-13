using System.Numerics;

namespace AgeOfGold.Navigation;

/// <summary>Builds the host's compressed bounding tree over boxes: the structure its navigation queries descend to find a face.</summary>
internal static class BoundingTree
{
    /// <param name="Nodes">Six bytes per node, parents before children, a node's first child directly after it.</param>
    internal sealed record Tree(byte[] Nodes, Vector3 Min, Vector3 Max);

    // A node narrows its parent's box on each axis by two four-bit steps, each standing for its square over this divisor.
    private const float Steps = 226;
    private const byte Inner = 0x80;

    internal static Tree Build(IReadOnlyList<(Vector3 Min, Vector3 Max)> boxes)
    {
        if (boxes.Count == 0 || boxes.Count >= 1 << 23) throw new InvalidDataException("A bounding tree needs between one and eight million boxes");
        foreach (var (low, high) in boxes)
            if (!Finite(low) || !Finite(high) || low.X > high.X || low.Y > high.Y || low.Z > high.Z) throw new InvalidDataException("Invalid box for a bounding tree");
        var min = boxes.Select(b => b.Min).Aggregate(Vector3.Min); var max = boxes.Select(b => b.Max).Aggregate(Vector3.Max);
        var nodes = new byte[6 * (2 * boxes.Count - 1)];
        int next = 0;
        void Write(int[] members, Vector3 parentMin, Vector3 parentMax)
        {
            int index = next++;
            var node = nodes.AsSpan(index * 6, 6);
            var low = members.Select(m => boxes[m].Min).Aggregate(Vector3.Min); var high = members.Select(m => boxes[m].Max).Aggregate(Vector3.Max);
            // The stored box is the parent's, pulled in as far as whole steps allow without cutting into the members.
            Span<float> storedLow = stackalloc float[3], storedHigh = stackalloc float[3];
            for (int axis = 0; axis < 3; axis++)
            {
                float from = Axis(parentMin, axis), to = Axis(parentMax, axis), extent = to - from;
                int a = Step(Axis(low, axis) - from, extent), b = Step(to - Axis(high, axis), extent);
                node[axis] = (byte)(a << 4 | b);
                storedLow[axis] = from + a * a / Steps * extent; storedHigh[axis] = to - b * b / Steps * extent;
            }
            Vector3 innerMin = new(storedLow[0], storedLow[1], storedLow[2]), innerMax = new(storedHigh[0], storedHigh[1], storedHigh[2]);
            int value;
            if (members.Length == 1) value = members[0];
            else
            {
                // Split at the median along the axis on which the members' centres spread furthest.
                var centres = members.Select(m => (boxes[m].Min + boxes[m].Max) / 2).ToArray();
                var spread = centres.Aggregate(Vector3.Max) - centres.Aggregate(Vector3.Min);
                int axis = spread.X >= spread.Y && spread.X >= spread.Z ? 0 : spread.Y >= spread.Z ? 1 : 2;
                var ordered = members.OrderBy(m => Axis(boxes[m].Min + boxes[m].Max, axis)).ThenBy(m => m).ToArray();
                value = ordered.Length / 2;
                node[3] = Inner;
                Write(ordered[..value], innerMin, innerMax);
                Write(ordered[value..], innerMin, innerMax);
            }
            // An inner node counts its first child's leaves, which places its second child; a leaf names its box.
            node = nodes.AsSpan(index * 6, 6);
            node[3] |= (byte)(value >> 16); node[4] = (byte)value; node[5] = (byte)(value >> 8);
        }
        Write(Enumerable.Range(0, boxes.Count).ToArray(), min, max);
        return new(nodes, min, max);
    }

    // The largest whole step whose square does not overshoot the gap; rounding only ever leaves the box larger.
    private static int Step(float gap, float extent)
    {
        if (!(extent > 0) || !(gap > 0)) return 0;
        int step = Math.Min(15, (int)MathF.Sqrt(gap / extent * Steps));
        while (step > 0 && step * step / Steps * extent > gap) step--;
        return step;
    }

    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
