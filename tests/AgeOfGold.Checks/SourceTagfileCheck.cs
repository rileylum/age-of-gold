using System.Buffers.Binary;
using System.Text;
using AgeOfGold.Collision;
using HKLib.Reflection.hk2018;
using HKLib.Serialization.hk2018;
using HKLib.Serialization.hk2018.Binary;
using Flags = HKLib.Reflection.HavokType.Member.MemberFlags;

internal static class SourceTagfileCheck
{
    internal static void Run()
    {
        var raw = Fixture();
        var root = new SourceTagfile(raw).Root;
        Require(root.Field("count").Integer() == 6, "authored scalar differs");
        Require(root.Field("values").Elements().Select(v => v.Integer()).SequenceEqual(new long[] { 2, 5, 8, 13, 21, 34 }),
            "authored item array differs");
        Require(root.Field("vector").Floats().SequenceEqual(new float[] { -2.5f, 3, 7.25f, 1 }), "authored inline vector differs");
        foreach (string mutation in new[] { "version", "pointer", "range", "type" })
        {
            var broken = Fixture(mutation);
            try
            {
                _ = new SourceTagfile(broken).Root.Field("values").Elements();
                throw new Exception($"Accepted invalid {mutation}");
            }
            catch (InvalidDataException) { }
        }
        // Rewriting: an array's contents are replaced, the data is laid out again, and the recorded pointer positions follow
        // their items. This authored file keeps its root after its array, so the root moves when the array grows.
        var movable = Fixture("root-last");
        var before = new SourceTagfile(movable);
        int item = before.Root.Field("values").Item;
        var longer = new uint[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5 };
        var bytes = new byte[longer.Length * 4];
        for (int i = 0; i < longer.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), longer[i]);
        var rewritten = before.Rewrite(new Dictionary<int, (byte[], int)> { [item] = (bytes, longer.Length) });
        Require(rewritten.SequenceEqual(before.Rewrite(new Dictionary<int, (byte[], int)> { [item] = (bytes, longer.Length) })), "tagfile rewrite does not repeat");
        var after = new SourceTagfile(rewritten).Root;
        Require(after.Field("values").Elements().Select(v => (uint)v.Integer()).SequenceEqual(longer), "rewritten array differs");
        Require(after.Field("count").Integer() == 6 && after.Field("vector").Floats().SequenceEqual(new float[] { -2.5f, 3, 7.25f, 1 }), "rewriting an array changed other values");
        // The one recorded pointer position is the root's array field, sixteen bytes into the root, wherever the root now lies.
        var (dataStart, patch) = Patch(rewritten);
        Require(BinaryPrimitives.ReadUInt64LittleEndian(rewritten.AsSpan(dataStart + patch)) == (ulong)item && patch % 8 == 0 && patch >= longer.Length * 4 + 16,
            "pointer position did not follow its item");
        Require(BinaryPrimitives.ReadInt32BigEndian(rewritten) == rewritten.Length, "rewritten file states the wrong length");
        Require(before.Rewrite(new Dictionary<int, (byte[], int)>()).AsSpan().SequenceEqual(movable), "an empty rewrite changed the file");
        foreach (var bad in new Dictionary<int, (byte[], int)>[] { new() { [item] = (bytes, 7) }, new() { [1] = (new byte[48], 1) }, new() { [9] = (bytes, 11) } })
        {
            try { before.Rewrite(bad); throw new Exception("Accepted an invalid array replacement"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: source tagfile reads authored metadata, inline values and item arrays; arrays rewrite with pointer positions following; invalid references fail");
    }

    private static byte[] Fixture(string? mutation = null)
    {
        var common = HavokType.Optional.Format | HavokType.Optional.SizeAlign;
        var integer = new HavokTypeBuilder().WithName("hkUint32").WithFormat((32 << 10) | 4)
            .WithSizeAlignment(4, 4).WithOptionals(common).Build();
        var real = new HavokTypeBuilder().WithName("float").WithFormat((23 << 16) | 5)
            .WithSizeAlignment(4, 4).WithOptionals(common).Build();
        var vector = new HavokTypeBuilder().WithName("T[N]").WithTemplateParameter("tT", real).WithTemplateParameter("vN", 4)
            .WithFormat((4 << 8) | 8).WithSizeAlignment(16, 4).WithSubType(real)
            .WithOptionals(common | HavokType.Optional.SubType).Build();
        var array = new HavokTypeBuilder().WithName("hkArray").WithTemplateParameter("tT", integer)
            .WithFormat(8).WithSizeAlignment(16, 8).WithSubType(integer)
            .WithOptionals(common | HavokType.Optional.SubType).Build();
        var record = new HavokTypeBuilder().WithName("AuthoredCollisionFixture").WithFormat(7).WithSizeAlignment(48, 8)
            .WithOptionals(common | HavokType.Optional.Members).WithField("count", Flags.Field, 4, integer)
            .WithField("values", Flags.Field, 16, array).WithField("vector", Flags.Field, 32, vector).Build();
        using var definitions = new MemoryStream();
        new HavokBinarySerializer(new HavokTypeRegistry([])).Write(
            new HavokCompendium([1], [integer, real, vector, array, record]), definitions);
        // The library supplies the type encoding; the independent item table and values are authored here.
        var compendium = definitions.ToArray();
        int typeOffset = 8 + (BinaryPrimitives.ReadInt32BigEndian(compendium.AsSpan(8)) & 0x0fffffff);
        var type = compendium[typeOffset..];
        for (int at = 8; at < type.Length;)
        {
            var name = Encoding.ASCII.GetString(type, at + 4, 4);
            if (name == "TNA1") "TNAM"u8.CopyTo(type.AsSpan(at + 4));
            if (name == "TBDY") "TBOD"u8.CopyTo(type.AsSpan(at + 4));
            at += BinaryPrimitives.ReadInt32BigEndian(type.AsSpan(at)) & 0x0fffffff;
        }
        // The root record (48 bytes) and its array (24 bytes): the root first, or last for the rewriting check.
        bool rootLast = mutation == "root-last";
        int rootAt = rootLast ? 24 : 0, arrayAt = rootLast ? 0 : 48;
        var data = new byte[72];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(rootAt + 4), 6);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(rootAt + 16), mutation == "pointer" ? 99UL : 2UL);
        float[] values = [-2.5f, 3, 7.25f, 1];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(rootAt + 32 + i * 4), values[i]);
        int[] numbers = [2, 5, 8, 13, 21, 34];
        for (int i = 0; i < numbers.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(arrayAt + i * 4), numbers[i]);
        var items = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(items.AsSpan(12), 0x10000005);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(16), rootAt);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(items.AsSpan(24), mutation == "type" ? 0x20000002u : 0x20000001u);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(28), mutation == "range" ? 68 : arrayAt);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(32), 6);
        // One recorded pointer position, listed under the array field's type: the root's array field.
        var patches = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(patches, 4); BinaryPrimitives.WriteInt32LittleEndian(patches.AsSpan(4), 1); BinaryPrimitives.WriteInt32LittleEndian(patches.AsSpan(8), rootAt + 16);
        return Chunk("TAG0", false,
            Chunk("SDKV", true, Encoding.ASCII.GetBytes(mutation == "version" ? "20990100" : "20150100")),
            Chunk("DATA", true, data), type,
            Chunk("INDX", false, Chunk("ITEM", true, items), Chunk("PTCH", true, patches)));
    }

    // Where a file's data begins, and its one recorded pointer position within the data.
    private static (int DataStart, int Patch) Patch(byte[] file)
    {
        int data = 8 + 16, at = 8;
        while (Encoding.ASCII.GetString(file, at + 4, 4) != "INDX") at += BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at)) & 0x0fffffff;
        int item = at + 8, patch = item + (BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(item)) & 0x0fffffff);
        return (data + 8, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(patch + 16)));
    }

    internal static byte[] Chunk(string name, bool leaf, params byte[][] contents)
    {
        var result = new byte[8 + contents.Sum(b => b.Length)];
        BinaryPrimitives.WriteInt32BigEndian(result, result.Length | (leaf ? 0x40000000 : 0));
        Encoding.ASCII.GetBytes(name).CopyTo(result, 4);
        int offset = 8;
        foreach (var bytes in contents) { bytes.CopyTo(result, offset); offset += bytes.Length; }
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
