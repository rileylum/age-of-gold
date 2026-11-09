using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgeOfGold.Runtime;

internal sealed record HostBinding(uint ImageSize, uint WorldSlot, string Fingerprint)
{
    internal static HostBinding Read(string executable, string toolchain)
    {
        var bytes = File.ReadAllBytes(executable);
        var image = new ExecutableImage(bytes);
        if (image.Version != new Version(2, 7, 2, 0))
            throw new InvalidDataException("Unsupported host runtime layout; expected executable version 2.7.2.0");
        var decode = new NativeDisassembler(toolchain, executable, image);
        var table = WorldTable(image);
        var functions = new HashSet<ExecutableImage.Function>();
        foreach (var reference in image.RelativeReferences([table]))
        {
            var f = image.Containing(reference);
            if (f is null) continue;
            if (decode.Read(f).Any(i => i.Op == "lea" && i.Operand.Contains("[rip", StringComparison.Ordinal)
                && decode.RipTarget(i.Operand) == table)) functions.Add(f);
        }
        var starts = functions.Select(f => f.Start).ToHashSet();
        var callers = new HashSet<ExecutableImage.Function>();
        foreach (var reference in image.RelativeReferences(starts))
        {
            var f = image.Containing(reference - 1);
            if (f is not null && reference + 4 <= f.End && image.Bytes[image.Raw(reference - 1)] == 0xe8) callers.Add(f);
        }
        var candidates = new HashSet<int>();
        foreach (var f in callers)
        {
            var code = decode.Read(f);
            for (var i = 0; i < code.Length; i++)
            {
                if (code[i].Op != "call" || decode.DirectTarget(code[i].Operand) is not int target || !starts.Contains(target)) continue;
                var slot = ReturnStore(code, i + 1, decode);
                if (slot is int address && address % 8 == 0
                    && image.Has(address, 8, SectionCharacteristics.MemRead | SectionCharacteristics.MemWrite, SectionCharacteristics.MemExecute))
                    candidates.Add(address);
            }
        }
        if (candidates.Count != 1) throw new InvalidDataException($"Expected one host world-manager slot; found {candidates.Count} from {functions.Count} type functions and {callers.Count} callers");
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        using var final = File.OpenRead(executable);
        if (Convert.ToHexString(SHA256.HashData(final)) != fingerprint)
            throw new InvalidDataException("Host executable changed during runtime binding discovery");
        return new(image.Size, (uint)candidates.Single(), fingerprint);
    }

    internal byte[] Write()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("AOGSTATE"));
        foreach (ushort part in new ushort[] { 2, 7, 2, 0 }) writer.Write(part);
        writer.Write(ImageSize); writer.Write(WorldSlot); writer.Write(Convert.FromHexString(Fingerprint));
        // Only the pointer links and values consumed by the lighting runtime are described.
        foreach (uint field in new uint[] { 0x10ef8, 0x190, 0x68, 0x6d0, 0x70, 0xa8, 0x18, 0xd0 }) writer.Write(field);
        return stream.ToArray();
    }

    private static int WorldTable(ExecutableImage image)
    {
        var names = image.Find(Encoding.ASCII.GetBytes(".?AVWorldChrManImp@CS@@\0")).ToArray();
        if (names.Length != 1) throw new InvalidDataException("Host world-manager type metadata is missing or ambiguous");
        var descriptor = names[0] - 16;
        var tables = new HashSet<int>();
        foreach (var reference in image.Find(BitConverter.GetBytes(descriptor)))
        {
            var locator = reference - 12;
            if (!image.Has(locator, 24, SectionCharacteristics.MemRead, SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute)) continue;
            var raw = image.Raw(locator, 24);
            if (image.U32(raw) != 1 || image.U32(raw + 4) != 0 || image.U32(raw + 8) != 0 || image.U32(raw + 20) != locator) continue;
            foreach (var pointer in image.Find(BitConverter.GetBytes(image.Base + (uint)locator)))
            {
                var table = pointer + 8;
                if (pointer % 8 != 0 || !image.Has(table, 8, SectionCharacteristics.MemRead,
                    SectionCharacteristics.MemWrite | SectionCharacteristics.MemExecute)) continue;
                var entry = image.U64(image.Raw(table, 8));
                if (entry >= image.Base && entry - image.Base < image.Size && image.Has((int)(entry - image.Base), 1, SectionCharacteristics.MemExecute))
                    tables.Add(table);
            }
        }
        if (tables.Count != 1) throw new InvalidDataException("Host world-manager primary virtual table is missing or ambiguous");
        return tables.Single();
    }

    private static int? ReturnStore(NativeDisassembler.Instruction[] code, int at, NativeDisassembler decode)
    {
        var visited = new HashSet<int>();
        // lazy: only unconditional, return-preserving constructor exits; extend after another supported pattern is proven.
        for (var step = 0; step < 16 && at >= 0 && at < code.Length && visited.Add(at); step++)
        {
            var i = code[at];
            if (i.Op == "jmp" && decode.DirectTarget(i.Operand) is int target)
                at = Array.FindIndex(code, x => x.Address == target);
            else if (i.Op == "nop") at++;
            else if (i.Op == "mov" && Regex.IsMatch(i.Operand, @"^qword ptr \[rip [+-] 0x[0-9a-f]+\], rax\s+#", RegexOptions.CultureInvariant))
                return decode.RipTarget(i.Operand);
            else return null;
        }
        return null;
    }
}
