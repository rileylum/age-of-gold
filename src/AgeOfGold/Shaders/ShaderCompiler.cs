using System.Diagnostics;

namespace AgeOfGold.Shaders;

internal sealed class ShaderCompiler
{
    private readonly string executable;
    private readonly string assembler;
    internal const string Version = "1.9.2609.5";

    internal ShaderCompiler(string executable, string assembler)
    {
        this.executable = Path.GetFullPath(executable);
        this.assembler = Path.GetFullPath(assembler);
        foreach (var path in new[] { this.executable, this.assembler })
            if (!File.Exists(path)) throw new FileNotFoundException("External shader tool not found", path);
        if (!Run(this.executable, ["--version"]).Contains(Version, StringComparison.Ordinal))
            throw new InvalidDataException($"DXC {Version} is required");
    }

    internal byte[] Compile(string source, string entry, string profile) => Temporary(root =>
    {
        var input = Path.Combine(root, "source.hlsl");
        var output = Path.Combine(root, "shader.dxil");
        File.WriteAllText(input, source);
        Run(executable, ["-T", profile, "-E", entry, "-Fo", output, "-Qstrip_debug", input]);
        return File.ReadAllBytes(output);
    });

    internal string Disassemble(byte[] shader) => Temporary(root =>
    {
        var input = Path.Combine(root, "shader.dxil");
        File.WriteAllBytes(input, shader);
        return Run(executable, ["-dumpbin", input]);
    });

    internal byte[] Assemble(string module, byte[] template) => Temporary(root =>
    {
        var input = Path.Combine(root, "module.ll");
        var original = Path.Combine(root, "template.dxil");
        var output = Path.Combine(root, "shader.dxil");
        File.WriteAllText(input, module);
        File.WriteAllBytes(original, template);
        Run(assembler, [input, output, original, "--refresh"]);
        return File.ReadAllBytes(output);
    });

    private static T Temporary<T>(Func<string, T> action)
    {
        var root = Directory.CreateTempSubdirectory("aog-shader-").FullName;
        try { return action(root); }
        finally { Directory.Delete(root, true); }
    }

    private static string Run(string tool, string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidDataException($"{Path.GetFileName(tool)} failed: {errors.Result}");
        return output.Result;
    }
}
