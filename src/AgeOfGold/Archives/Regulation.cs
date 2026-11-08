using SoulsFormats;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace AgeOfGold.Archives;

internal sealed class Regulation
{
    private readonly byte[] key;
    private readonly byte[] initialization;
    internal BND4 Tables { get; }

    private Regulation(byte[] key, byte[] initialization, BND4 tables)
    {
        this.key = key;
        this.initialization = initialization;
        Tables = tables;
    }

    internal static Regulation Read(byte[] executable, byte[] encrypted)
    {
        if (encrypted.Length < 32 || encrypted.Length % 16 != 0)
            throw new InvalidDataException("Regulation archive is truncated or has an unsupported encrypted length");
        using var aes = Aes.Create();
        var initialization = encrypted[..16];
        var block = new byte[16];
        BND4? tables = null;
        var key = Discover(executable, candidate =>
        {
            aes.Key = candidate;
            aes.DecryptCbc(encrypted.AsSpan(16, 16), initialization, block, PaddingMode.None);
            if (!block.AsSpan().StartsWith("BND4"u8) && !block.AsSpan().StartsWith("DCX\0"u8)) return false;
            try
            {
                var decoded = BND4.Read(aes.DecryptCbc(encrypted.AsSpan(16), initialization, PaddingMode.None));
                if (decoded.Files.Count == 0 || decoded.Files.Any(f => !f.Name.EndsWith(".param", StringComparison.OrdinalIgnoreCase)))
                    return false;
                tables = decoded;
                return true;
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException
                or FormatException or OverflowException)
            { return false; }
        });
        return new Regulation(key, initialization, tables!);
    }

    internal byte[] Write()
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return [.. initialization, .. aes.EncryptCbc(Tables.Write(), initialization, PaddingMode.PKCS7)];
    }

    internal static byte[] Discover(byte[] executable, Func<byte[], bool> validates)
    {
        byte[]? found = null;
        bool zeroChecked = false;
        try
        {
            using var pe = new PEReader(new MemoryStream(executable, false));
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32Plus)
                throw new InvalidDataException("Expected a 64-bit host executable");
            foreach (var section in pe.PEHeaders.SectionHeaders)
            {
                var flags = section.SectionCharacteristics;
                if (!flags.HasFlag(SectionCharacteristics.MemRead)
                    || flags.HasFlag(SectionCharacteristics.MemExecute) || flags.HasFlag(SectionCharacteristics.MemWrite)) continue;
                if (section.PointerToRawData < 0 || section.SizeOfRawData < 0
                    || section.PointerToRawData > executable.Length - section.SizeOfRawData)
                    throw new InvalidDataException("Host executable data section exceeds the file");
                var data = executable.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                // lazy: aligned key blobs; investigate a new layout when an executable has no validated candidate.
                for (int at = 0; at <= data.Length - 32; at += 16)
                {
                    var candidate = data.Slice(at, 32);
                    if (!candidate.ContainsAnyExcept((byte)0))
                    {
                        if (zeroChecked) continue;
                        zeroChecked = true;
                    }
                    if (found is not null && candidate.SequenceEqual(found)) continue;
                    var bytes = candidate.ToArray();
                    if (!validates(bytes)) continue;
                    if (found is not null) throw new InvalidDataException("Ambiguous regulation key discovery in host executable");
                    found = bytes;
                }
            }
        }
        catch (BadImageFormatException error)
        { throw new InvalidDataException("Host executable has an unsupported PE image", error); }
        return found ?? throw new InvalidDataException("No key in the host executable validates the regulation archive");
    }
}
