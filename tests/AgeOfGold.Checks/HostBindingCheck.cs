using System.Buffers.Binary;
using System.Text;
using AgeOfGold.Runtime;

internal static class HostBindingCheck
{
    internal static void Fixture(string output)
    {
        output = AgeOfGold.Install.Canonical(output);
        if (!AgeOfGold.Install.Within(output, AgeOfGold.Install.Canonical(Path.GetTempPath()))
            || Directory.Exists(output) || File.Exists(output)) throw new ArgumentException("Use a fresh fixture directory under system temp");
        Directory.CreateDirectory(output);
        var executable = Path.Combine(output, "authored.exe");
        File.WriteAllBytes(executable, Image());
        var binding = HostBinding.Read(executable, Environment.GetEnvironmentVariable("AOG_LLVM_MINGW")
            ?? throw new ArgumentException("Set AOG_LLVM_MINGW"));
        File.WriteAllBytes(Path.Combine(output, "host-state.bin"), binding.Write());
    }

    internal static void Run(string root)
    {
        var toolchain = Environment.GetEnvironmentVariable("AOG_LLVM_MINGW")
            ?? throw new ArgumentException("Set AOG_LLVM_MINGW");
        var path = Path.Combine(root, "authored-host.exe");
        File.WriteAllBytes(path, Image());
        var first = HostBinding.Read(path, toolchain);
        Require(first.WorldSlot == 0x3000 && first.ImageSize == 0x6000, "Authored world slot not found");
        Require(first == HostBinding.Read(path, toolchain), "Discovery is not deterministic");
        File.WriteAllBytes(path, Image(imageBase: 0x180000000));
        Require(HostBinding.Read(path, toolchain).WorldSlot == 0x3000, "Relocation changed relative binding");
        var zeroFilled = Image(); U32(zeroFilled, 0x1e0, 0x400); U32(zeroFilled, 0x44c, 0x3300 - 0x1050);
        File.WriteAllBytes(path, zeroFilled);
        Require(HostBinding.Read(path, toolchain).WorldSlot == 0x3300, "Zero-filled data slot was rejected");
        void Reject(byte[] bytes, string reason)
        {
            File.WriteAllBytes(path, bytes);
            try { HostBinding.Read(path, toolchain); }
            catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
            throw new Exception("Accepted invalid host: " + reason);
        }
        Reject(Image(major: 3), "Unsupported host runtime layout");
        var missing = Image(); missing[0x610] = 0;
        Reject(missing, "type metadata");
        var duplicate = Image(); Encoding.ASCII.GetBytes(".?AVWorldChrManImp@CS@@\0").CopyTo(duplicate, 0x6c0);
        Reject(duplicate, "type metadata");
        var noTable = Image(); noTable[0x650] = 0;
        Reject(noTable, "virtual table");
        var readOnly = Image(); U32(readOnly, 0x1fc, 0x40000040);
        Reject(readOnly, "found 0");
        var conditional = Image(); conditional[0x445] = 0x74;
        Reject(conditional, "found 0");
        var loop = Image(); loop[0x446] = 0xfe;
        Reject(loop, "found 0");
        var clobber = Image(); new byte[] { 0x31, 0xc0, 0x90, 0x90 }.CopyTo(clobber, 0x445);
        Reject(clobber, "found 0");
        var outside = Image(); outside[0x446] = 0x60;
        Reject(outside, "found 0");
        var ambiguous = Image(); Array.Copy(ambiguous, 0x440, ambiguous, 0x480, 17);
        U32(ambiguous, 0x481, unchecked(0x1000u - 0x1085u)); U32(ambiguous, 0x48c, 0x3010 - 0x1090);
        U32(ambiguous, 0x124, 36);
        U32(ambiguous, 0xa18, 0x1080); U32(ambiguous, 0xa1c, 0x1091); U32(ambiguous, 0xa20, 0x20a0);
        Reject(ambiguous, "found 2");
        var overlap = Image(); U32(overlap, 0x1bc, 0x1000);
        Reject(overlap, "Overlapping");
        var resource = Image(); U32(resource, 0xc44, 0x80000000);
        Reject(resource, "resource leaf");
        Require(first.Write().Length == 88 && first.Write().SequenceEqual(first.Write()), "Runtime config encoding differs");
        Console.WriteLine("PASS: authored executable binding, relocation, repeatability and rejected versions/metadata/control flow");
    }

    // Every byte is authored: two tiny functions, standard PE/RTTI metadata and a version resource.
    internal static byte[] Image(ulong imageBase = 0x140000000, ushort major = 2)
    {
        var b = new byte[0x1000];
        void U16(int p, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), v);
        void U32(int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p), v);
        void U64(int p, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(p), v);
        void Text(int p, string s) => Encoding.ASCII.GetBytes(s).CopyTo(b, p);
        b[0] = (byte)'M'; b[1] = (byte)'Z'; U32(60, 0x80); Text(0x80, "PE\0\0");
        U16(0x84, 0x8664); U16(0x86, 5); U16(0x94, 240); U16(0x96, 0x22);
        const int opt = 0x98;
        U16(opt, 0x20b); U32(opt + 16, 0x1040); U32(opt + 20, 0x1000); U64(opt + 24, imageBase);
        U32(opt + 32, 0x1000); U32(opt + 36, 0x200); U16(opt + 40, 6); U16(opt + 48, 6);
        U32(opt + 56, 0x6000); U32(opt + 60, 0x400); U16(opt + 68, 3); U32(opt + 108, 16);
        U32(opt + 128, 0x5000); U32(opt + 132, 0x180);
        U32(opt + 136, 0x4000); U32(opt + 140, 24);
        var names = new[] { ".text", ".rdata", ".data", ".pdata", ".rsrc" };
        for (var i = 0; i < names.Length; i++)
        {
            var p = opt + 240 + i * 40;
            Text(p, names[i]); U32(p + 8, 0x200); U32(p + 12, (uint)(i + 1) * 0x1000);
            U32(p + 16, 0x200); U32(p + 20, 0x400 + (uint)i * 0x200);
            U32(p + 36, i == 0 ? 0x60000020u : i == 2 ? 0xc0000040u : 0x40000040u);
        }
        // lea rax, [rip + table]; ret. The table has one entry pointing back to this function.
        new byte[] { 0x48, 0x8d, 0x05 }.CopyTo(b, 0x400); U32(0x403, 0x2088 - 0x1007); b[0x407] = 0xc3;
        // call; jump over an unreachable null result; store the returned pointer; ret.
        b[0x440] = 0xe8; U32(0x441, unchecked(0x1000u - 0x1045u));
        b[0x445] = 0xeb; b[0x446] = 2; b[0x447] = 0x31; b[0x448] = 0xc0;
        new byte[] { 0x48, 0x89, 0x05 }.CopyTo(b, 0x449); U32(0x44c, 0x3000 - 0x1050); b[0x450] = 0xc3;
        Text(0x610, ".?AVWorldChrManImp@CS@@\0");
        U32(0x650, 1); U32(0x65c, 0x2000); U32(0x664, 0x2050);
        U64(0x680, imageBase + 0x2050); U64(0x688, imageBase + 0x1000);
        U32(0xa00, 0x1000); U32(0xa04, 0x1008); U32(0xa08, 0x20a0);
        U32(0xa0c, 0x1040); U32(0xa10, 0x1051); U32(0xa14, 0x20a0); b[0x6a0] = 1;
        // Resource tree: RT_VERSION -> identity -> language -> VS_FIXEDFILEINFO.
        U16(0xc0e, 1); U32(0xc10, 16); U32(0xc14, 0x80000018);
        U16(0xc26, 1); U32(0xc28, 1); U32(0xc2c, 0x80000030);
        U16(0xc3e, 1); U32(0xc40, 0x409); U32(0xc44, 0x48);
        U32(0xc48, 0x5060); U32(0xc4c, 92);
        U16(0xc60, 92); U16(0xc62, 52);
        Encoding.Unicode.GetBytes("VS_VERSION_INFO\0").CopyTo(b, 0xc66);
        U32(0xc88, 0xfeef04bd); U32(0xc8c, 0x10000); U32(0xc90, ((uint)major << 16) | 7);
        U32(0xc94, 2 << 16); U32(0xc98, ((uint)major << 16) | 7); U32(0xc9c, 2 << 16);
        U32(0xca0, 0x3f); U32(0xca8, 0x40004); U32(0xcac, 1);
        return b;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void U32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
}
