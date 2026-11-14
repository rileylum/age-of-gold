using System.Buffers.Binary;

namespace AgeOfGold.Navigation;

/// <summary>Writes the host's per-map navigation index: the meshes a map loads, where they join, and the gates between them.</summary>
internal static class HostNavigationIndex
{
    // The index's sections by identity: the mesh list, the joins between meshes and their edge pairs, the gates, and the
    // map-wide settings that do not refer to any mesh.
    private const int Meshes = 0, Joins = 4, JoinedEdges = 5, Gates = 8, MeshEntry = 144, GateEntry = 48, GateDistances = 16;
    private static readonly int[] MapWide = [9, 10, 11];
    // A gate's walk to a gate it cannot reach, and the longest walk that still fits, in centimetres.
    private const ushort NoWalk = ushort.MaxValue, LongestWalk = ushort.MaxValue - 1;

    /// <summary>
    /// An index listing one mesh per piece, numbered by <paramref name="models"/>. Entries follow the template's first, named
    /// on from its name. Face references, drops and conditions, which describe the template's own meshes, are dropped.
    /// </summary>
    // lazy: only the host's first mesh set is written; its second set, listed under a second name range, is left out
    // until a host character is found that needs it.
    internal static byte[] Write(byte[] template, IReadOnlyList<int> models, NavigationPieces.Division division)
    {
        var pieces = division.Pieces;
        if (pieces.Length == 0 || models.Count != pieces.Length || models.Distinct().Count() != models.Count) throw new InvalidDataException("A navigation index needs one distinct model for each piece");
        if (pieces.Any(p => p.Triangles.Length == 0)) throw new InvalidDataException("A navigation index entry needs faces");
        if (pieces.Any(p => p.Gates.Length > GateDistances)) throw new InvalidDataException("A navigation piece has more gates than the host index holds distances for");
        if (template.Length < 16 || !template.AsSpan(0, 4).SequenceEqual("NVMA"u8) || BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(4)) != 8
            || BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(8)) != template.Length) throw new InvalidDataException("Unsupported host navigation index");
        int count = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(12));
        var output = new List<byte>(template.AsSpan(0, 16).ToArray());
        void Int(List<byte> to, int value) => to.AddRange(BitConverter.GetBytes(value));
        var names = new uint[pieces.Length];
        var written = new HashSet<int>();
        int at = 16;
        for (int i = 0; i < count; i++)
        {
            if (at + 16 > template.Length) throw new InvalidDataException("Truncated host navigation index");
            int id = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at)), size = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at + 8));
            int entries = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at + 12));
            if (size < 0 || at + 16 + size > template.Length) throw new InvalidDataException("Truncated host navigation index");
            var body = template.AsSpan(at + 16, size).ToArray();
            var kept = new List<byte>();
            if (id == Meshes)
            {
                if (entries <= 0 || size != entries * MeshEntry || BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at + 4)) != 4) throw new InvalidDataException("Unsupported host navigation mesh list");
                // An entry: placement, name, model, first face in the map-wide numbering, face count, then its gates' range.
                // The template's first entry supplies the placement and settings every installed entry shares.
                uint first = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(48));
                int face = 0, gate = 0;
                for (int p = 0; p < pieces.Length; p++)
                {
                    var entry = body.AsSpan(0, MeshEntry).ToArray();
                    names[p] = checked(first + (uint)p);
                    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(48), names[p]); BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(52), models[p]);
                    BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(56), face); BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(64), pieces[p].Triangles.Length);
                    BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(72), checked((short)gate)); BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(74), (short)pieces[p].Gates.Length);
                    // The flag that marks a mesh without faces is cleared.
                    BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(76), 0);
                    kept.AddRange(entry); face += pieces[p].Triangles.Length; gate += pieces[p].Gates.Length;
                }
                entries = pieces.Length;
            }
            else if (id == Joins)
            {
                // The mesh list comes first in every installed index, so the names are known here.
                if (!written.Contains(Meshes)) throw new InvalidDataException("Host navigation index lists joins before meshes");
                int edge = 0;
                foreach (var join in division.Joins)
                {
                    Int(kept, (int)names[join.From]); Int(kept, (int)names[join.To]); Int(kept, join.Edges.Length); Int(kept, 0); Int(kept, edge); Int(kept, 0); Int(kept, 0); Int(kept, 0);
                    edge += join.Edges.Length;
                }
                entries = division.Joins.Length;
            }
            else if (id == JoinedEdges)
            {
                foreach (var (face, edge, otherFace, otherEdge) in division.Joins.SelectMany(j => j.Edges)) { Int(kept, face); Int(kept, edge); Int(kept, otherFace); Int(kept, otherEdge); }
                entries = division.Joins.Sum(j => j.Edges.Length);
            }
            else if (id == Gates)
            {
                foreach (var piece in pieces)
                    foreach (var gate in piece.Gates)
                    {
                        var entry = new byte[GateEntry];
                        BinaryPrimitives.WriteSingleLittleEndian(entry, gate.Position.X); BinaryPrimitives.WriteSingleLittleEndian(entry.AsSpan(4), gate.Position.Y); BinaryPrimitives.WriteSingleLittleEndian(entry.AsSpan(8), gate.Position.Z);
                        BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(12), checked((short)gate.Other)); BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(14), checked((short)gate.Counterpart));
                        for (int d = 0; d < GateDistances; d++)
                            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(16 + 2 * d), d < gate.Distances.Length && gate.Distances[d] is { } metres ? (ushort)MathF.Min(LongestWalk, MathF.Round(metres * 100)) : NoWalk);
                        kept.AddRange(entry);
                    }
                entries = pieces.Sum(p => p.Gates.Length);
            }
            else if (MapWide.Contains(id)) kept.AddRange(body);
            else entries = 0;
            output.AddRange(template.AsSpan(at, 8).ToArray());
            Int(output, kept.Count); Int(output, entries); output.AddRange(kept);
            written.Add(id); at += 16 + size;
        }
        if (at != template.Length || !new[] { Meshes, Joins, JoinedEdges, Gates }.All(written.Contains)) throw new InvalidDataException("Host navigation index has trailing data or lacks a section this build writes");
        var result = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), result.Length);
        return result;
    }
}
