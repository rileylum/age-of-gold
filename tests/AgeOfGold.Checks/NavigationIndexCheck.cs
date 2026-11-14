using System.Buffers.Binary;
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

        var written = HostNavigationIndex.OneMesh(template, 1500, 77);
        if (!written.SequenceEqual(HostNavigationIndex.OneMesh(template, 1500, 77))) throw new Exception("Navigation index does not repeat");
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
        // One mesh: the template's entry for that model, starting the face numbering, with the new face count and no nodes.
        var expected = Mesh(100100001, 1500, 0, 77, 0, 0);
        if (read[0].Count != 1 || read[0].Version != 4 || !read[0].Body.SequenceEqual(expected)) throw new Exception("Navigation index mesh entry differs");
        // Everything that referred to the template's meshes is gone; the map-wide settings stay.
        foreach (int emptied in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 12, 13 })
            if (read[emptied].Count != 0 || read[emptied].Body.Length != 0) throw new Exception($"Navigation index section {emptied} still refers to the template's meshes");
        if (!read[11].Body.SequenceEqual(marker) || !read[10].Body.SequenceEqual(settings) || read[9].Count != 1 || read[9].Body.Length != 16) throw new Exception("Navigation index settings changed");
        foreach (var bad in new Action[] { () => HostNavigationIndex.OneMesh(template, 4242, 77), () => HostNavigationIndex.OneMesh(template, 1500, 0),
            () => HostNavigationIndex.OneMesh(template[..^8], 1500, 77), () => { var other = template.ToArray(); other[4] = 5; HostNavigationIndex.OneMesh(other, 1500, 77); } })
        {
            try { bad(); }
            catch (InvalidDataException) { continue; }
            throw new Exception("An invalid navigation index request was accepted");
        }
        Console.WriteLine("PASS: a one-mesh navigation index keeps the template's settings and that mesh's entry, and drops every reference to other meshes");
    }
}
