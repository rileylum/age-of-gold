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
        Console.WriteLine("PASS: source tagfile reads authored metadata, inline values and item arrays; invalid references fail");
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
        var data = new byte[72];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 6);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16), mutation == "pointer" ? 99UL : 2UL);
        float[] values = [-2.5f, 3, 7.25f, 1];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(32 + i * 4), values[i]);
        int[] numbers = [2, 5, 8, 13, 21, 34];
        for (int i = 0; i < numbers.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(48 + i * 4), numbers[i]);
        var items = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(items.AsSpan(12), 0x10000005);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(items.AsSpan(24), mutation == "type" ? 0x20000002u : 0x20000001u);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(28), mutation == "range" ? 68 : 48);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(32), 6);
        return Chunk("TAG0", false,
            Chunk("SDKV", true, Encoding.ASCII.GetBytes(mutation == "version" ? "20990100" : "20150100")),
            Chunk("DATA", true, data), type,
            Chunk("INDX", false, Chunk("ITEM", true, items), Chunk("PTCH", true, [])));
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
