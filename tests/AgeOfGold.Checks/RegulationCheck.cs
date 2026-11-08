using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AgeOfGold.Archives;
using SoulsFormats;

internal static class RegulationCheck
{
    internal static void Run()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)(13 + i * 7)).ToArray();
        byte[] other = Enumerable.Range(0, 32).Select(i => (byte)(251 - i * 5)).ToArray();
        byte[] iv = Enumerable.Range(0, 16).Select(i => (byte)(i * 11)).ToArray();
        var binder = new BND4 { Files = [new BinderFile(Binder.FileFlags.Flag1, 7, "authored.param", "authored table"u8.ToArray())] };
        var encoded = Encrypt(binder.Write(), key, iv);
        var exe = Executable(key, other);
        var read = Regulation.Read(exe, encoded);
        Require(read.Tables.Files.Single().Bytes.SequenceEqual("authored table"u8.ToArray()), "regulation contents differ");
        read.Tables.Files[0].Bytes = "authored changed table"u8.ToArray();
        var rebuilt = read.Write();
        Require(rebuilt.SequenceEqual(read.Write()), "regulation encryption is not deterministic");
        var checkedBinder = Regulation.Read(exe, rebuilt);
        Require(checkedBinder.Tables.Files.Single().Bytes.SequenceEqual("authored changed table"u8.ToArray()), "regulation change did not survive encryption");
        Require(rebuilt.AsSpan(0, 16).SequenceEqual(iv), "regulation initialization vector changed");
        Reject(() => Regulation.Read(Executable(other, key), encoded), "key in executable code was accepted");
        Reject(() => Regulation.Read(exe, encoded[..^1]), "truncated encrypted archive was accepted");
        var duplicate = Executable(key, other);
        key.CopyTo(duplicate, 608);
        Require(Regulation.Read(duplicate, encoded).Tables.Files.Count == 1, "duplicate locations made one key ambiguous");
        other.CopyTo(duplicate, 672);
        // Two keys opening one ciphertext are impractical to construct; exercise that policy with a validator fake.
        Reject(() => Regulation.Discover(duplicate, candidate => candidate.SequenceEqual(key) || candidate.SequenceEqual(other)),
            "ambiguous key discovery was accepted");
        Console.WriteLine("PASS: regulation key is discovered from data, edits round trip deterministically, missing/ambiguous keys fail");
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static byte[] Encrypt(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return [.. iv, .. aes.EncryptCbc(plain, iv, PaddingMode.PKCS7)];
    }

    // A tiny PE image authored here; neither section contains executable game code.
    private static byte[] Executable(byte[] dataKey, byte[] codeKey)
    {
        var bytes = new byte[1536];
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        "MZ"u8.CopyTo(bytes); U32(60, 128);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(128));
        U16(132, 0x8664); U16(134, 2); U16(148, 240); U16(150, 0x22);
        U16(152, 0x20b); U32(184, 4096); U32(188, 512); U32(208, 12288); U32(212, 512); U32(260, 16);
        void Section(int header, string name, uint raw, uint address, uint flags)
        {
            Encoding.ASCII.GetBytes(name).CopyTo(bytes, header);
            U32(header + 8, 512); U32(header + 12, address); U32(header + 16, 512);
            U32(header + 20, raw); U32(header + 36, flags);
        }
        Section(392, ".rdata", 512, 4096, 0x40000040);
        Section(432, ".text", 1024, 8192, 0x60000020);
        dataKey.CopyTo(bytes, 544); codeKey.CopyTo(bytes, 1088);
        return bytes;
    }
}
