using System.Buffers.Binary;

namespace AgeOfGold.Navigation;

/// <summary>Writes the host's per-map navigation index, which names the meshes a map loads.</summary>
internal static class HostNavigationIndex
{
    // The index's sections by identity: the mesh list, and the map-wide settings that do not refer to any mesh.
    private const int Meshes = 0, MeshEntry = 144;
    private static readonly int[] MapWide = [9, 10, 11];

    /// <summary>
    /// An index listing one mesh: the template's entry for <paramref name="model"/> with a new face count. Connections,
    /// nodes and face references, which describe the template's own meshes, are dropped.
    /// </summary>
    // lazy: one mesh for the whole map, so no connections between meshes; several meshes need the connection sections.
    internal static byte[] OneMesh(byte[] template, int model, int faces)
    {
        if (faces <= 0) throw new InvalidDataException("A navigation index entry needs faces");
        if (template.Length < 16 || !template.AsSpan(0, 4).SequenceEqual("NVMA"u8) || BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(4)) != 8
            || BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(8)) != template.Length) throw new InvalidDataException("Unsupported host navigation index");
        int count = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(12));
        var output = new List<byte>(template.AsSpan(0, 16).ToArray());
        int at = 16; bool listed = false;
        for (int i = 0; i < count; i++)
        {
            if (at + 16 > template.Length) throw new InvalidDataException("Truncated host navigation index");
            int id = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at)), size = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at + 8));
            int entries = BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at + 12));
            if (size < 0 || at + 16 + size > template.Length) throw new InvalidDataException("Truncated host navigation index");
            var body = template.AsSpan(at + 16, size).ToArray();
            byte[] kept = [];
            if (id == Meshes)
            {
                if (entries <= 0 || size != entries * MeshEntry || BinaryPrimitives.ReadInt32LittleEndian(template.AsSpan(at + 4)) != 4) throw new InvalidDataException("Unsupported host navigation mesh list");
                // An entry: placement, name, model, first face in the map-wide numbering, face count, then its node range.
                var entry = Enumerable.Range(0, entries).Select(e => body.AsSpan(e * MeshEntry, MeshEntry).ToArray())
                    .FirstOrDefault(e => BinaryPrimitives.ReadInt32LittleEndian(e.AsSpan(52)) == model) ?? throw new InvalidDataException("Host navigation index lists no mesh for model " + model);
                BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(56), 0); BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(64), faces);
                BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(72), 0);
                kept = entry; entries = 1; listed = true;
            }
            else if (MapWide.Contains(id)) kept = body;
            else entries = 0;
            output.AddRange(template.AsSpan(at, 8).ToArray());
            output.AddRange(BitConverter.GetBytes(kept.Length)); output.AddRange(BitConverter.GetBytes(entries)); output.AddRange(kept);
            at += 16 + size;
        }
        if (at != template.Length || !listed) throw new InvalidDataException("Host navigation index has trailing data or no mesh list");
        var result = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), result.Length);
        return result;
    }
}
