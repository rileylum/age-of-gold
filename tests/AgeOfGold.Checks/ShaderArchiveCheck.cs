using System.Buffers.Binary;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class ShaderArchiveCheck
{
    internal static void Run()
    {
        var old = Program("DXIL", 17);
        var replacement = Program("DXIL", 37);
        var family = new BND4 { Version = "AUTHORED", Files = [
            new(Binder.FileFlags.Flag1, 41, @"C:\authored\surface.ppo", old),
            new(Binder.FileFlags.Flag1, 42, @"C:\authored\surface.meta", [3, 1, 4, 1, 5]),
        ] };
        var outer = new BND4 { Version = "AUTHORED", Files = [
            new(Binder.FileFlags.Flag1, 73, @"C:\authored\surface.shaderbdle", family.Write()),
            new(Binder.FileFlags.Flag1, 74, "other.bin", [2, 7, 1, 8]),
        ] };
        string[] path = ["surface.shaderbdle", "surface.ppo"];
        var input = outer.Write();
        Require(ShaderArchive.Read(input, path).SequenceEqual(old), "nested stage read differs");
        var actual = ShaderArchive.Replace(input, path, replacement);
        Require(ShaderArchive.Read(actual, path).SequenceEqual(replacement), "nested stage was not replaced");
        Require(ShaderArchive.Replace(actual, path, old).SequenceEqual(input), "other bytes or binder metadata changed");
        Require(ShaderArchive.Replace(input, path, replacement).SequenceEqual(actual), "shader archive rewrite differs across runs");
        var compressed = outer.Write(new DCX.DcxDfltCompressionInfo(DCX.DfltCompressionPreset.DCX_DFLT_10000_24_9));
        var compressedEdit = ShaderArchive.Replace(compressed, path, replacement);
        Require(DCX.Is(compressedEdit) && ShaderArchive.Replace(compressedEdit, path, old).SequenceEqual(compressed),
            "shader rewrite lost the original container compression");
        var source = new BND3 { Version = "AUTHORED", Files = [new(Binder.FileFlags.Flag1, 9, "flat.fpo", Program("SHEX", 53))] };
        Require(ShaderArchive.Read(source.Write(), "flat.fpo").SequenceEqual(source.Files[0].Bytes), "source stage read differs");
        var added = ShaderArchive.AddFamily(input, "surface", "aog_surface",
            (suffix, bytes) => suffix == ".ppo" ? replacement : suffix == ".meta" ? bytes : throw new Exception("Unexpected family member " + suffix));
        Require(ShaderArchive.Read(added, "aog_surface.shaderbdle", "aog_surface.ppo").SequenceEqual(replacement)
            && ShaderArchive.Read(added, path).SequenceEqual(old), "added family stage or its template differs");
        var addedOuter = BND4.Read(added); var addedEntry = addedOuter.Files.Single(f => f.Name == @"C:\authored\aog_surface.shaderbdle");
        var addedInner = BND4.Read(addedEntry.Bytes);
        Require(addedEntry.ID == 75 && addedEntry.Flags == Binder.FileFlags.Flag1
            && addedInner.Files.Select(f => (f.ID, f.Name)).SequenceEqual(new[] { (41, @"C:\authored\aog_surface.ppo"), (42, @"C:\authored\aog_surface.meta") })
            && addedInner.Files[1].Bytes.SequenceEqual(new byte[] { 3, 1, 4, 1, 5 }), "added family identity or unedited member differs");
        addedOuter.Files.Remove(addedEntry);
        Require(addedOuter.Write().SequenceEqual(input), "adding a family changed existing entries");
        Require(ShaderArchive.AddFamily(input, "surface", "aog_surface", (suffix, bytes) => suffix == ".ppo" ? replacement : bytes).SequenceEqual(added),
            "family addition differs across runs");
        Reject(() => ShaderArchive.AddFamily(added, "surface", "aog_surface", (_, bytes) => bytes), "occupied family");
        Reject(() => ShaderArchive.AddFamily(input, "missing", "aog_surface", (_, bytes) => bytes), "missing template family");
        Reject(() => ShaderArchive.AddFamily(input, "surface", "aog/../x", (_, bytes) => bytes), "invalid family name");
        Console.WriteLine("PASS: source and nested host shader stages read and rewrite deterministically, preserving unrelated bytes");
        Reject(() => ShaderArchive.Read(input), "empty path");
        Reject(() => ShaderArchive.Read(input, "../surface.shaderbdle"), "path traversal");
        Reject(() => ShaderArchive.Read(input, "missing.ppo"), "missing entry");
        family.Files.Add(new(Binder.FileFlags.Flag1, 43, "elsewhere/SURFACE.PPO", old));
        Reject(() => ShaderArchive.Read(family.Write(), "surface.ppo"), "ambiguous leaf name");
        Reject(() => ShaderArchive.Read(input, "other.bin", "stage.ppo"), "non-container intermediate entry");
        Reject(() => ShaderArchive.Replace(input, path, [1, 2, 3]), "invalid replacement");
        foreach (var (at, value) in new[] { (20, 7u), (24, 65u), (28, uint.MaxValue), (32, 0u), (36, 40u), (44, uint.MaxValue) })
        {
            var corrupt = old.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(at, 4), value);
            Reject(() => ShaderArchive.Replace(input, path, corrupt), "malformed program part index");
        }
        var missing = Program("NONE", 13);
        Reject(() => ShaderArchive.Replace(input, path, missing), "missing program payload");
        var multiple = old.ToArray();
        "SHEX"u8.CopyTo(multiple.AsSpan(52));
        Reject(() => ShaderArchive.Replace(input, path, multiple), "ambiguous program payload");
        Require(input.SequenceEqual(outer.Write()), "failed edit changed input bytes");
        Console.WriteLine("PASS: missing or ambiguous stages, invalid paths and malformed program containers fail before rewriting");
    }

    internal static byte[] Program(string kind, int payload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("DXBC"u8); writer.Write(new byte[16]); writer.Write(1); writer.Write(64); writer.Write(2);
        writer.Write(40); writer.Write(52);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(kind)); writer.Write(4); writer.Write(payload);
        writer.Write("TEST"u8); writer.Write(4); writer.Write(71);
        return stream.ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action, string description)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Accepted " + description);
    }
}
