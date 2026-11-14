using System.Buffers.Binary;
using System.Numerics;
using AgeOfGold.Navigation;

internal static class NavigationIndexCheck
{
    internal static void Run()
    {
        // An authored index in the host's layout: a header, then sections of {identity, version, size, count} and entries.
        // Section 0 lists meshes in 144-byte entries; this one lists two, with connectors and nodes that refer to them.
        byte[] Section(int id, int version, int count, byte[] body)
        {
            var section = new byte[16 + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(section, id); BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(4), version);
            BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(8), body.Length); BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(12), count);
            body.CopyTo(section, 16);
            return section;
        }
        byte[] Mesh(int name, int model, int firstFace, int faces, short firstNode, short nodes)
        {
            var entry = new byte[144];
            BinaryPrimitives.WriteSingleLittleEndian(entry.AsSpan(12), 1);
            for (int i = 0; i < 3; i++) BinaryPrimitives.WriteSingleLittleEndian(entry.AsSpan(32 + 4 * i), 1);
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(48), name); BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(52), model);
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(56), firstFace); BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(64), faces);
            BinaryPrimitives.WriteSingleLittleEndian(entry.AsSpan(68), 2.4f);
            BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(72), firstNode); BinaryPrimitives.WriteInt16LittleEndian(entry.AsSpan(74), nodes);
            entry[80] = 1; entry[81] = 0xFF;
            return entry;
        }
        byte[] marker = [0x88, 0xCD, 6, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], settings = Enumerable.Range(0, 32).Select(i => (byte)(i * 3)).ToArray();
        var sections = new[]
        {
            Section(11, 1, 1, marker), Section(0, 4, 2, [.. Mesh(100100000, 1000, 0, 224, 0, 2), .. Mesh(100100001, 1500, 224, 635, 2, 3)]), Section(1, 1, 0, []),
            Section(2, 1, 1, new byte[40]), Section(3, 1, 2, new byte[256]), Section(4, 1, 3, new byte[96]), Section(5, 1, 5, new byte[80]), Section(6, 1, 0, []), Section(7, 1, 0, []),
            Section(8, 1, 5, new byte[240]), Section(9, 1, 1, new byte[16]), Section(10, 1, 1, settings), Section(12, 1, 0, []), Section(13, 1, 0, []),
        };
        var template = new byte[16 + sections.Sum(s => s.Length)];
        "NVMA"u8.CopyTo(template); BinaryPrimitives.WriteInt32LittleEndian(template.AsSpan(4), 8);
        BinaryPrimitives.WriteInt32LittleEndian(template.AsSpan(8), template.Length); BinaryPrimitives.WriteInt32LittleEndian(template.AsSpan(12), sections.Length);
        int at = 16;
        foreach (var section in sections) { section.CopyTo(template, at); at += section.Length; }

        // Three squares in a row; the outer two are one piece and the middle one another, so the pieces meet at two gates.
        var vertices = Enumerable.Range(0, 4).SelectMany(i => new[] { new Vector3(i, 0, 0), new Vector3(i, 0, 1) }).ToArray();
        var triangles = Enumerable.Range(0, 3).SelectMany(i => new[] { (2 * i, 2 * i + 1, 2 * i + 3), (2 * i, 2 * i + 3, 2 * i + 2) }).ToArray();
        var division = NavigationPieces.Split(vertices, triangles, ["n0001", "n0001", "n0002", "n0002", "n0001", "n0001"]);
        int[] models = [800005, 800035];
        var written = HostNavigationIndex.Write(template, models, division);
        if (!written.SequenceEqual(HostNavigationIndex.Write(template, models, division))) throw new Exception("Navigation index does not repeat");
        if (!written.AsSpan(0, 8).SequenceEqual(template.AsSpan(0, 8)) || BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(8)) != written.Length
            || BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(12)) != 14) throw new Exception("Navigation index header differs");
        var read = new Dictionary<int, (int Version, int Count, byte[] Body)>(); var order = new List<int>();
        for (at = 16; at < written.Length;)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(at)), size = BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(at + 8));
            read.Add(id, (BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(at + 4)), BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(at + 12)), written.AsSpan(at + 16, size).ToArray()));
            order.Add(id); at += 16 + size;
        }
        if (!order.SequenceEqual(new[] { 11, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 13 })) throw new Exception("Navigation index sections are reordered or lost");
        // One entry per piece, named on from the template's first name: its model, its faces in the map-wide numbering, its gates.
        byte[] expected = [.. Mesh(100100000, 800005, 0, 4, 0, 2), .. Mesh(100100001, 800035, 4, 2, 2, 2)];
        if (read[0].Count != 2 || read[0].Version != 4 || !read[0].Body.SequenceEqual(expected)) throw new Exception("Navigation index mesh entries differ");
        // Joins: each way names both pieces, how many edge pairs it has and where they start; a pair is a face and edge on each side.
        int[] Ints(byte[] body) => Enumerable.Range(0, body.Length / 4).Select(i => BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(i * 4))).ToArray();
        if (read[4].Count != 2 || !Ints(read[4].Body).SequenceEqual([100100000, 100100001, 2, 0, 0, 0, 0, 0, 100100001, 100100000, 2, 0, 2, 0, 0, 0])) throw new Exception("Navigation index joins differ");
        if (read[5].Count != 4 || !Ints(read[5].Body).SequenceEqual([1, 4, 0, 0, 2, 6, 1, 4, 0, 0, 1, 4, 1, 4, 2, 6])) throw new Exception("Navigation index joined edges differ");
        // Gates: a position, the neighbouring piece, the gate's place among that piece's gates, then the walk in centimetres
        // to each gate of its own piece, with every unused or unreachable place filled.
        if (read[8].Count != 4 || read[8].Body.Length != 4 * 48) throw new Exception("Navigation index gates differ in number");
        for (int node = 0; node < 4; node++)
        {
            var bytes = read[8].Body.AsSpan(node * 48, 48).ToArray(); int piece = node / 2, gate = node % 2;
            var position = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(8)));
            var distances = Enumerable.Range(0, 16).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(16 + 2 * i))).ToArray();
            ushort between = piece == 0 ? ushort.MaxValue : (ushort)MathF.Round(division.Pieces[1].Gates[0].Distances[1]!.Value * 100);
            if (position != new Vector3(1 + gate, 0, 0.5f) || BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(12)) != 1 - piece || BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(14)) != gate
                || distances[gate] != 0 || distances[1 - gate] != between || between < 100 || distances.Skip(2).Any(d => d != ushort.MaxValue))
                throw new Exception($"Navigation index gate {node} differs");
        }
        // Everything else that referred to the template's meshes is gone; the map-wide settings stay.
        foreach (int emptied in new[] { 1, 2, 3, 6, 7, 12, 13 })
            if (read[emptied].Count != 0 || read[emptied].Body.Length != 0) throw new Exception($"Navigation index section {emptied} still refers to the template's meshes");
        if (!read[11].Body.SequenceEqual(marker) || !read[10].Body.SequenceEqual(settings) || read[9].Count != 1 || read[9].Body.Length != 16) throw new Exception("Navigation index settings changed");
        foreach (var bad in new Action[] { () => HostNavigationIndex.Write(template, [800005], division), () => HostNavigationIndex.Write(template, [800005, 800005], division),
            () => HostNavigationIndex.Write(template[..^8], models, division), () => { var other = template.ToArray(); other[4] = 5; HostNavigationIndex.Write(other, models, division); } })
        {
            try { bad(); }
            catch (InvalidDataException) { continue; }
            throw new Exception("An invalid navigation index request was accepted");
        }
        Console.WriteLine("PASS: a navigation index lists each piece with its faces, joins and gates, keeps the template's settings and drops its other references");
    }
}
