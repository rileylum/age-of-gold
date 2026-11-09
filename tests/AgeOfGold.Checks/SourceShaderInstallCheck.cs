using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
        int graphics = 0, rejected = 0, converted = 0, bindings = 0, repeated = 0, buffers = 0;
        var programs = Directory.GetFiles(source.Locate("shader"), "*DX11*.shaderbnd.dcx").Order(StringComparer.Ordinal)
            .SelectMany(path => BND3.Read(source.Read(Path.GetRelativePath(source.Root, path))).Files
                .Select(file => (Archive: Path.GetFileName(path), File: file))).ToArray();
        foreach (var (_, file) in programs.Where(p => Path.GetExtension(p.File.Name) != ".cpo"))
        {
            try { SourceShaderConstants.Normalize(file.Bytes); }
            catch (InvalidDataException e) { throw new InvalidDataException("Constant metadata in " + file.Name + ": " + e.Message, e); }
        }
        Console.WriteLine("Validated all installed graphics constant metadata before compilation");
        foreach (var (archive, file) in programs)
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
            var first = converter.Convert(file.Bytes);
            if (archive == "FRPG_Deferred_DX11.shaderbnd.dcx"
                || Path.GetFileName(file.Name.Replace('\\', '/')) == "FRPG_Phn_DifSpcBmpMul___Sdw_HemEnv.fpo")
            {
                if (!first.SequenceEqual(converter.Convert(file.Bytes))) throw new Exception("Installed shader conversion is not deterministic");
                repeated++;
            }
            ShaderArchive.ValidateProgram(first);
            var text = compiler.Disassemble(first).Replace("\r\n", "\n");
            if (!text.Contains("main")) throw new Exception("Converted program cannot be read back");
            var expectedBindings = SourceShaderBindings.Read(file.Bytes).ToDictionary(b => b.Name);
            int bound = 0;
            foreach (Match row in Regex.Matches(text, @"^; (\S+)\s+(?:cbuffer|sampler|texture)\s+\S+\s+\S+\s+\S+\s+(cb|s|t)(\d+)\s+(\d+)\s*$", RegexOptions.Multiline))
            {
                if (!expectedBindings.TryGetValue(row.Groups[1].Value, out var binding)
                    || row.Groups[2].Value != (binding.Register == 'b' ? "cb" : binding.Register.ToString())
                    || uint.Parse(row.Groups[3].Value) != binding.Slot || row.Groups[4].Value != "1")
                    throw new Exception("Converted resource binding differs: " + file.Name + " " + row.Value);
                bound++;
            }
            if (bound != expectedBindings.Count) throw new Exception("Converted resource inventory differs: " + file.Name);
            foreach (var buffer in SourceShaderConstants.Read(file.Bytes))
            {
                string marker = "; cbuffer " + buffer.Name + "\n";
                int start = text.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0) throw new Exception("Converted constant buffer missing: " + file.Name);
                int end = text.IndexOf("; cbuffer ", start + marker.Length, StringComparison.Ordinal);
                if (end < 0) end = text.IndexOf("; Resource Bindings:", start, StringComparison.Ordinal);
                var sizes = Regex.Matches(text[start..end], @"^;   } " + Regex.Escape(buffer.Name)
                    + @";\s*; Offset:\s*0 Size:\s*(\d+)\s*$", RegexOptions.Multiline);
                if (sizes.Count != 1 || int.Parse(sizes[0].Groups[1].Value) != buffer.Bytes)
                    throw new Exception("Converted constant register extent differs: " + file.Name + " " + buffer.Name);
                buffers++;
            }
            bindings += bound;
            converted++;
            if (converted % 200 == 0) Console.WriteLine($"Checked {converted} installed programs and {bindings} resource bindings");
        }
        if (graphics == 0 || converted == 0 || rejected == 0) throw new Exception("Source shader corpus was not exercised");
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(install.Locate(input.Key)))) != input.Value)
                    throw new Exception("Install changed");
        Console.WriteLine($"PASS: {graphics} installed graphics stages convert and preserve {bindings} bindings and {buffers} constant buffers; {repeated} repeat exactly; {rejected} compute stages rejected and installs unchanged");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
