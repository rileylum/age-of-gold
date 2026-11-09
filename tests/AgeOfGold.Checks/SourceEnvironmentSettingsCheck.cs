using System.Buffers.Binary;
using System.Text;
using AgeOfGold;
using AgeOfGold.Shaders;

internal static class SourceEnvironmentSettingsCheck
{
    internal static void Run(string root)
    {
        var directory = Path.Combine(root, "authored-environment-settings");
        Directory.CreateDirectory(directory);
        var toolchain = Environment.GetEnvironmentVariable("AOG_LLVM_MINGW") ?? throw new ArgumentException("Set AOG_LLVM_MINGW");
        SourceEnvironmentSettings Read(byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(directory, "DarkSoulsRemastered.exe"), bytes);
            return SourceEnvironmentSettings.Read(new Install(directory, "DarkSoulsRemastered.exe"), toolchain);
        }
        Require(Read(Image()) == new SourceEnvironmentSettings(5, 2.75f), "Environment mode/gain were not derived");
        Require(Read(Image(0x180000000)) == new SourceEnvironmentSettings(5, 2.75f), "Relocation changed environment settings");
        var changed = Image(); Float(changed, 0x740, 0.375f);
        Require(Read(changed).Strength == 0.375f, "Environment gain ignored installed input");
        var mode = Image(); mode[0x407] = 9;
        Require(Read(mode).LightingType == 9, "Material mode was embedded");
        var argument = Image(); new byte[] { 0x48, 0x8b, 0x0b }.CopyTo(argument, 0x439);
        Require(Read(argument) == new SourceEnvironmentSettings(5, 2.75f), "Independent argument load changed the packed value");
        void Reject(byte[] bytes, string reason)
        {
            try { Read(bytes); }
            catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
            throw new Exception("Accepted invalid environment settings: " + reason);
        }
        var flag = Image(); flag[0x8e0] = (byte)'x'; Reject(flag, "missing or ambiguous");
        var branch = Image(); branch[0x408] = 0x75; Reject(branch, "found 0");
        var escape = Image(); escape[0x412] = 0x70; Reject(escape, "found 0");
        var dataCall = Image(); U32(dataCall, 0x401, 0x2140 - 0x1005); Reject(dataCall, "found 0");
        var vector = Image(); vector[0x427] = 0x15; Reject(vector, "found 0");
        var output = Image(); output[0x438] = 0x40; Reject(output, "found 0");
        var clobber = Image(); new byte[] { 0x49, 0x89, 0xc8 }.CopyTo(clobber, 0x439); Reject(clobber, "found 0");
        var duplicate = Image(); Array.Copy(duplicate, 0x400, duplicate, 0x500, 66);
        foreach (var offset in new[] { 1, 12, 23, 34, 61 })
            U32(duplicate, 0x500 + offset, unchecked(BinaryPrimitives.ReadUInt32LittleEndian(duplicate.AsSpan(0x500 + offset)) - 0x100));
        U32(duplicate, 0xa10, 0x1142); Reject(duplicate, "found 2");
        foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            var invalid = Image(); Float(invalid, 0x740, value); Reject(invalid, "gain");
        }
        Console.WriteLine("PASS: authored environment mode/gain discovery, relocation, changed values and rejected control flow");
    }

    private static byte[] Image(ulong imageBase = 0x140000000)
    {
        var b = HostBindingCheck.Image(imageBase);
        Array.Clear(b, 0x400, 0x200);
        U32(b, 0xc90, (1u << 16) | 3); U32(b, 0xc94, 1u << 16);
        void Setting(int at, string name, uint address, uint kind)
        {
            Encoding.Unicode.GetBytes(name + "\0").CopyTo(b, at);
            U32(b, at + 100, kind); U64(b, at + 104, imageBase + address);
        }
        Setting(0x800, "m_dbgAmbientStrength", 0x31c0, 2);
        Setting(0x870, "m_dbgNormalStrength", 0x31c4, 2);
        Setting(0x8e0, "m_dbgGIEnvLightMode", 0x31c8, 3);
        Float(b, 0x9c0, 0.625f); Float(b, 0x9c4, 0.875f); b[0x9c8] = 1;
        Float(b, 0x740, 2.75f);
        // Authored x64: material-mode/flag gate, a packed vector, and an argument passed to a setter.
        byte[] code = [
            0xe8, 0, 0, 0, 0, 0x83, 0xf8, 5, 0x74, 9,
            0x80, 0x3d, 0, 0, 0, 0, 0, 0x74, 0x32,
            0xf3, 0x0f, 0x10, 0x2d, 0, 0, 0, 0,
            0x0f, 0x57, 0xd2,
            0xf3, 0x0f, 0x10, 0x25, 0, 0, 0, 0,
            0x0f, 0x14, 0xe5, 0x0f, 0x14, 0xd1, 0x0f, 0x14, 0xe2,
            0x0f, 0x29, 0x64, 0x24, 0x30,
            0x4c, 0x8d, 0x44, 0x24, 0x30,
            0x48, 0x89, 0xca,
            0xe8, 0, 0, 0, 0, 0xc3,
        ];
        code.CopyTo(b, 0x400);
        U32(b, 0x401, 0x1100 - 0x1005);
        U32(b, 0x40c, 0x31c8 - 0x1011);
        U32(b, 0x417, 0x31c4 - 0x101b);
        U32(b, 0x422, 0x2140 - 0x1026);
        U32(b, 0x43d, 0x1100 - 0x1041);
        b[0x412] = 0x1041 - 0x1013;
        b[0x500] = 0xc3;
        U32(b, 0xa00, 0x1000); U32(b, 0xa04, 0x1042);
        U32(b, 0xa0c, 0x1100); U32(b, 0xa10, 0x1101);
        return b;
    }

    private static void Float(byte[] b, int at, float value) => U32(b, at, BitConverter.SingleToUInt32Bits(value));
    private static void U32(byte[] b, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
    private static void U64(byte[] b, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at), value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
