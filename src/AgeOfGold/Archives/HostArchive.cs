using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace AgeOfGold.Archives;

internal sealed class HostArchive
{
    private sealed record Entry(string Data, int Size, int ContentSize, long Offset, byte[]? Key, (int Start, int Length)[] Ranges);
    private readonly Install install;
    private readonly Dictionary<ulong, List<Entry>> entries = [];

    internal HostArchive(Install install)
    {
        this.install = install;
        var keys = Keys(install.Read("eldenring.exe")).ToArray();
        if (keys.Length == 0) throw new InvalidDataException("Host executable contains no supported archive keys");
        var headers = Directory.GetFiles(install.Root, "*.bhd").Order(StringComparer.Ordinal).ToArray();
        if (headers.Length == 0) throw new InvalidDataException("Host install contains no archive headers");
        foreach (var path in headers)
        {
            var name = Path.GetFileName(path);
            var cipher = install.Read(name);
            byte[]? plain = null;
            foreach (var key in keys)
            {
                var candidate = Decrypt(cipher, key);
                if (candidate.AsSpan().StartsWith("BHD5"u8))
                {
                    plain = candidate;
                    break;
                }
            }
            if (plain is null) throw new InvalidDataException($"No executable key opens archive {name}");
            Index(plain, Path.ChangeExtension(name, ".bdt"));
        }
    }

    internal bool Contains(string relative) => entries.ContainsKey(Hash(Install.Relative(relative).ToLowerInvariant()));

    private static ulong Hash(string name)
    {
        ulong hash = 0;
        foreach (char c in "/" + name) hash = unchecked(hash * 133 + c);
        return hash;
    }

    internal byte[] Read(string relative)
    {
        var name = Install.Relative(relative).ToLowerInvariant();
        if (!entries.TryGetValue(Hash(name), out var matches)) throw new FileNotFoundException($"Host archive entry not found: {name}");
        var bytes = Read(matches[0]);
        foreach (var other in matches.Skip(1))
            if (!bytes.AsSpan().SequenceEqual(Read(other)))
                throw new InvalidDataException($"Conflicting host archive entries for {name}: {string.Join(", ", matches.Select(e => e.Data))}");
        return bytes;
    }

    private byte[] Read(Entry entry)
    {
        using var stream = File.OpenRead(install.Locate(entry.Data));
        if (entry.Offset < 0 || entry.Offset > stream.Length - entry.Size)
            throw new InvalidDataException($"Host archive entry exceeds {entry.Data}");
        var bytes = new byte[entry.Size];
        stream.Position = entry.Offset;
        stream.ReadExactly(bytes);
        install.Fingerprints[$"{entry.Data}@{entry.Offset}+{entry.Size}"] = Convert.ToHexString(SHA256.HashData(bytes));
        if (entry.Key is not null)
        {
            using var aes = Aes.Create();
            aes.Key = entry.Key;
            foreach (var (start, length) in entry.Ranges)
                aes.DecryptEcb(bytes.AsSpan(start, length), bytes.AsSpan(start, length), PaddingMode.None);
        }
        // Some host entries omit the unpadded length; their stored extent still contains the file.
        return entry.ContentSize == 0 ? bytes : bytes[..entry.ContentSize];
    }

    private void Index(byte[] bytes, string data)
    {
        ReadOnlySpan<byte> Slice(long offset, long size)
        {
            if (offset < 0 || size < 0 || offset > bytes.Length - size)
                throw new InvalidDataException($"Archive header range exceeds its length: {data}");
            return bytes.AsSpan((int)offset, (int)size);
        }
        int Int(long offset) => BinaryPrimitives.ReadInt32LittleEndian(Slice(offset, 4));
        long Long(long offset) => BinaryPrimitives.ReadInt64LittleEndian(Slice(offset, 8));
        if (!Slice(0, 4).SequenceEqual("BHD5"u8) || Slice(4, 1)[0] != 255 || Int(8) != 1)
            throw new InvalidDataException($"Unsupported archive version or byte order: {data}");
        int buckets = Int(16), bucketOffset = Int(20);
        Slice(bucketOffset, (long)buckets * 8);
        for (int i = 0; i < buckets; i++)
        {
            int count = Int(bucketOffset + i * 8L), offset = Int(bucketOffset + i * 8L + 4);
            Slice(offset, count * 40L);
            for (int j = 0; j < count; j++)
            {
                long at = offset + j * 40L;
                ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(Slice(at, 8));
                int size = Int(at + 8), contentSize = Int(at + 12);
                if (size < 0 || contentSize < 0 || contentSize > size)
                    throw new InvalidDataException($"Invalid archive entry length: {data}");
                long keyOffset = Long(at + 32);
                byte[]? key = null;
                var ranges = new List<(int, int)>();
                if (keyOffset != 0)
                {
                    key = Slice(keyOffset, 16).ToArray();
                    int rangeCount = Int(keyOffset + 16);
                    Slice(keyOffset + 20, rangeCount * 16L);
                    for (int k = 0; k < rangeCount; k++)
                    {
                        long start = Long(keyOffset + 20 + k * 16L), end = Long(keyOffset + 28 + k * 16L);
                        if (start == -1 && end == -1) continue;
                        if (start < 0 || end < start || end > size || (end - start) % 16 != 0)
                            throw new InvalidDataException($"Invalid archive encryption range: {data}");
                        if (end != start) ranges.Add((checked((int)start), checked((int)(end - start))));
                    }
                }
                if (!entries.TryGetValue(hash, out var matches)) entries.Add(hash, matches = []);
                matches.Add(new(data, size, contentSize, Long(at + 16), key, ranges.ToArray()));
            }
        }
    }

    private static IEnumerable<RSAParameters> Keys(byte[] executable)
    {
        var text = Encoding.Latin1.GetString(executable);
        const string begin = "-----BEGIN RSA PUBLIC KEY-----", end = "-----END RSA PUBLIC KEY-----";
        for (int at = text.IndexOf(begin, StringComparison.Ordinal); at >= 0;
            at = text.IndexOf(begin, at + begin.Length, StringComparison.Ordinal))
        {
            int stop = text.IndexOf(end, at, StringComparison.Ordinal);
            if (stop < 0) throw new InvalidDataException("Unterminated archive key in host executable");
            RSAParameters key;
            try
            {
                var der = Convert.FromBase64String(text[(at + begin.Length)..stop]);
                var reader = new AsnReader(der, AsnEncodingRules.DER);
                var sequence = reader.ReadSequence();
                var modulus = sequence.ReadIntegerBytes().ToArray().SkipWhile(b => b == 0).ToArray();
                var exponent = sequence.ReadIntegerBytes().ToArray();
                sequence.ThrowIfNotEmpty(); reader.ThrowIfNotEmpty();
                key = new RSAParameters { Modulus = modulus, Exponent = exponent };
            }
            catch (Exception error) when (error is FormatException or AsnContentException)
            { throw new InvalidDataException("Invalid archive key in host executable", error); }
            yield return key;
        }
    }

    private static byte[] Decrypt(byte[] cipher, RSAParameters key)
    {
        int size = key.Modulus!.Length;
        if (size < 2 || cipher.Length % size != 0) return [];
        var modulus = new BigInteger(key.Modulus, true, true);
        var exponent = new BigInteger(key.Exponent!, true, true);
        var result = new byte[cipher.Length / size * (size - 1)];
        for (int at = 0, output = 0; at < cipher.Length; at += size, output += size - 1)
        {
            var value = BigInteger.ModPow(new BigInteger(cipher.AsSpan(at, size), true, true), exponent, modulus);
            var block = value.ToByteArray(true, true);
            if (block.Length >= size) return [];
            block.CopyTo(result, output + size - 1 - block.Length);
            if (at == 0 && !result.AsSpan().StartsWith("BHD5"u8)) return [];
        }
        return result;
    }
}
