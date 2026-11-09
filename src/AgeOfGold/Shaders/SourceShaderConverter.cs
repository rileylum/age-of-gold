using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgeOfGold.Shaders;

internal sealed class SourceShaderConverter
{
    private readonly string executable;
    private readonly ShaderCompiler compiler;
    internal const string Revision = "14bb0af4f9c074138df5a40d8d0bee0f30da0e30";
    internal string Fingerprint { get; }

    internal SourceShaderConverter(string directory, ShaderCompiler compiler)
    {
        directory = Install.Canonical(directory);
        var bytes = File.ReadAllBytes(Path.Combine(directory, "manifest.json"));
        using var manifest = JsonDocument.Parse(bytes);
        var metadata = manifest.RootElement;
        if (metadata.GetProperty("tool").GetString() != "HlslDecompiler" || metadata.GetProperty("revision").GetString() != Revision)
            throw new InvalidDataException("Unsupported shader converter revision");
        var files = metadata.GetProperty("files");
        string command = OperatingSystem.IsWindows() ? "HlslDecompiler.exe" : "HlslDecompiler";
        foreach (var required in new[] { command, "HlslDecompiler.dll", "HlslDecompiler.deps.json", "HlslDecompiler.runtimeconfig.json", "HlslDecompiler-LICENSE.txt" })
            if (!files.TryGetProperty(required, out _)) throw new InvalidDataException("Incomplete shader converter manifest");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.EnumerateObject())
        {
            string relative = Install.Relative(file.Name);
            string path = Install.Canonical(Path.Combine(directory, relative));
            if (!names.Add(relative) || !Install.Within(path, directory)) throw new InvalidDataException("Invalid shader converter manifest path");
            if (!File.Exists(path) || System.Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) != file.Value.GetString())
                throw new InvalidDataException("Shader converter fingerprint changed: " + relative);
        }
        Fingerprint = System.Convert.ToHexStringLower(SHA256.HashData(bytes));
        executable = Path.Combine(directory, command);
        this.compiler = compiler;
    }

    internal byte[] Convert(byte[] program)
    {
        string profile = Inspect(program);
        var root = Directory.CreateTempSubdirectory("aog-convert-shader-").FullName;
        try
        {
            string input = Path.Combine(root, "input.dxbc");
            File.WriteAllBytes(input, program);
            var start = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("--instructions");
            start.ArgumentList.Add(input);
            using var process = Process.Start(start)!;
            var source = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidDataException("Source shader conversion failed: " + error.Result);
            return compiler.CompileSourceInstructions(source.Result, profile);
        }
        finally { Directory.Delete(root, true); }
    }

    internal static string Inspect(byte[] program)
    {
        ShaderArchive.ValidateProgram(program);
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(program.AsSpan(at, 4));
        for (int part = 0; part < Word(28); part++)
        {
            int at = (int)Word(32 + part * 4);
            if (!program.AsSpan(at, 4).SequenceEqual("RDEF"u8)) continue;
            int size = (int)Word(at + 4);
            if (size < 16) throw new InvalidDataException("Truncated source resource definition");
            uint count = Word(at + 16), offset = Word(at + 20);
            if (offset > size || count > (size - offset) / 32)
                throw new InvalidDataException("Invalid source resource definition range");
            for (int item = 0; item < count; item++)
            {
                uint kind = Word(at + 8 + (int)offset + item * 32 + 4);
                if (kind is not (0 or 1 or 2 or 3 or 5 or 7))
                    throw new InvalidDataException("Source resource definition names a writable or unsupported resource");
            }
        }
        for (int part = 0; part < Word(28); part++)
        {
            int at = (int)Word(32 + part * 4);
            var tag = program.AsSpan(at, 4);
            if (!tag.SequenceEqual("SHEX"u8) && !tag.SequenceEqual("SHDR"u8)) continue;
            int length = (int)Word(at + 4);
            int payload = at + 8;
            if (length < 12 || length % 4 != 0 || Word(payload + 4) != length / 4)
                throw new InvalidDataException("Invalid DXBC instruction payload length");
            uint version = Word(payload);
            uint stage = version >> 16;
            if (stage > 1) throw new InvalidDataException("Only vertex and pixel source shaders are supported");
            // lazy: only SM4.0/4.1/5.0; a later instruction encoding needs a separately verified reader.
            if ((version & 0xffff) is not (0x40 or 0x41 or 0x50))
                throw new InvalidDataException("Unsupported source shader version");
            for (int token = payload + 8; token < payload + length;)
            {
                uint instruction = Word(token);
                uint opcode = instruction & 0x7ff;
                if (opcode is >= 155 and <= 160 or 163 or 164 or 166 or >= 168 and <= 190)
                    throw new InvalidDataException("Source shader uses writable resources or shared memory");
                // SwapC is unsupported by the pinned converter; newer feedback opcodes are unverified.
                if (opcode is 107 or 112 or 142 or 209 or >= 218)
                    throw new InvalidDataException($"Unsupported source shader opcode {opcode}");
                uint words;
                if (opcode == 53)
                {
                    if (token > payload + length - 8)
                        throw new InvalidDataException("Truncated custom-data length");
                    if ((instruction >> 11) > 4)
                        throw new InvalidDataException("Unsupported source shader custom-data class");
                    words = Word(token + 4);
                    if (words < 2) throw new InvalidDataException("Invalid custom-data length");
                }
                else words = (instruction >> 24) & 0x7f;
                if (words == 0 || words > (payload + length - token) / 4)
                    throw new InvalidDataException("Invalid source shader instruction length");
                token += (int)words * 4;
            }
            return stage == 0 ? "ps_6_0" : "vs_6_0";
        }
        throw new InvalidDataException("Source shader must contain DXBC instructions");
    }
}
