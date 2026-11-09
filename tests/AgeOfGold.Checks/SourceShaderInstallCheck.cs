using System.Security.Cryptography;
using AgeOfGold;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class SourceShaderInstallCheck
{
    internal static void Run()
    {
        if (Environment.GetEnvironmentVariable("CI") is not null) throw new InvalidOperationException("Install checks never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var converter = new SourceShaderConverter(Required("AOG_SHADER_CONVERTER"), compiler);
        int graphics = 0, rejected = 0, converted = 0;
        foreach (var path in Directory.GetFiles(source.Locate("shader"), "*DX11*.shaderbnd.dcx").Order(StringComparer.Ordinal))
        {
            var bytes = source.Read(Path.GetRelativePath(source.Root, path));
            var binder = BND3.Read(bytes);
            foreach (var file in binder.Files)
            {
                string extension = Path.GetExtension(file.Name);
                if (extension == ".cpo")
                {
                    try { SourceShaderConverter.Inspect(file.Bytes); }
                    catch (InvalidDataException e) when (e.Message.Contains("vertex and pixel") || e.Message.Contains("writable")) { rejected++; continue; }
                    throw new Exception("Installed compute shader was accepted");
                }
                string expected = extension switch { ".vpo" => "vs_6_0", ".fpo" => "ps_6_0", _ => throw new Exception("Unknown source shader stage") };
                if (SourceShaderConverter.Inspect(file.Bytes) != expected) throw new Exception("Installed shader stage differs from archive role");
                graphics++;
                if (Path.GetFileName(path) != "FRPG_Deferred_DX11.shaderbnd.dcx") continue;
                var first = converter.Convert(file.Bytes);
                if (!first.SequenceEqual(converter.Convert(file.Bytes))) throw new Exception("Installed shader conversion is not deterministic");
                ShaderArchive.ValidateProgram(first);
                if (!compiler.Disassemble(first).Contains("main")) throw new Exception("Converted program cannot be read back");
                converted++;
            }
        }
        if (graphics == 0 || converted == 0 || rejected == 0) throw new Exception("Source shader corpus was not exercised");
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(install.Locate(input.Key)))) != input.Value)
                    throw new Exception("Install changed");
        Console.WriteLine($"PASS: {graphics} installed graphics stages accepted, {rejected} compute stages rejected; {converted} deferred stages convert twice and read back");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
