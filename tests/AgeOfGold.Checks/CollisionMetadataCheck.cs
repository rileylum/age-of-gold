using HKLib.hk2018;
using HKLib.Reflection.hk2018;
using HKLib.Serialization.hk2018.Binary;
using Flags = HKLib.Reflection.HavokType.Member.MemberFlags;

internal static class CollisionMetadataCheck
{
    internal static void Run()
    {
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Res")))
            throw new Exception("Collision checks must run without bundled registry resources");
        foreach (int offset in new[] { 0, 12 })
        {
            // Deliberately invented layouts prove that serialized offsets, rather than a registry, win.
            var number = new HavokTypeBuilder().WithName("hkUint32")
                .WithFormat((32 << 10) | (int)HavokType.TypeKind.Int).WithSizeAlignment(4, 4)
                .WithOptionals(HavokType.Optional.Format | HavokType.Optional.SizeAlign).Build();
            var record = new HavokTypeBuilder().WithName("hkSimpleProperty")
                .WithFormat((int)HavokType.TypeKind.Record).WithSizeAlignment(20, 4)
                .WithOptionals(HavokType.Optional.Format | HavokType.Optional.SizeAlign | HavokType.Optional.Members)
                .WithField("key", Flags.Field, offset, number).WithHash(0x12345678).Build();
            var writer = new HavokBinarySerializer(new HavokTypeRegistry(new[] { number, record }));
            using var encoded = new MemoryStream();
            writer.Write(new hkSimpleProperty { m_key = 314159 }, encoded);
            var reader = new HavokBinarySerializer();
            var result = (hkSimpleProperty)reader.Read(new MemoryStream(encoded.ToArray()));
            Require(result.m_key == 314159, "serialized field offset was not respected");
            var description = reader.TypeRegistry.GetType("hkSimpleProperty")!;
            Require(description.Size == 20 && description.Fields.Single().Offset == offset,
                "serialized metadata was replaced by bundled descriptions");
            Require(description.Hash == 0x12345678, "serialized type hash was lost");
            using var repeated = new MemoryStream();
            reader.Write(result, repeated);
            Require(encoded.ToArray().SequenceEqual(repeated.ToArray()), "metadata round trip changed bytes");
        }
        Console.WriteLine("PASS: collision metadata comes from each input, without bundled registry resources");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
