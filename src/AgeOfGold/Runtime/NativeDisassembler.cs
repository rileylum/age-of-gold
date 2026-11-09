using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Runtime;

internal sealed class NativeDisassembler
{
    internal sealed record Instruction(int Address, string Op, string Operand);
    private readonly string executable;
    private readonly string host;
    private readonly ExecutableImage image;
    private readonly Dictionary<int, Instruction[]> cache = [];

    internal NativeDisassembler(string toolchain, string host, ExecutableImage image)
    {
        this.host = host;
        this.image = image;
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        executable = Path.Combine(toolchain, "bin", "llvm-objdump" + suffix);
        var compiler = Path.Combine(toolchain, "bin", "x86_64-w64-mingw32-clang" + suffix);
        var version = Run(compiler, ["--version"]);
        if (!version.StartsWith("clang version 23.1.5 (", StringComparison.Ordinal)
            || !version.Contains("392dfff97d29b59d77e5cac6d4b6ef2585fe7117", StringComparison.Ordinal)
            || !Run(executable, ["--version"]).Contains("LLVM version 23.1.5", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported LLVM-MinGW disassembler; use the pinned 20261104 distribution");
    }

    internal Instruction[] Read(ExecutableImage.Function function)
    {
        if (cache.TryGetValue(function.Start, out var previous)) return previous;
        // lazy: bound analysis to small constructor families; extend after a supported host needs more.
        if (cache.Count >= 64 || function.End - function.Start > 128 * 1024)
            throw new InvalidDataException("Host runtime discovery exceeds the supported function budget");
        var output = Run(executable, ["-d", "--no-show-raw-insn", "--x86-asm-syntax=intel",
            $"--start-address={image.Base + (uint)function.Start}", $"--stop-address={image.Base + (uint)function.End}", host]);
        var instructions = new List<Instruction>();
        foreach (var line in output.Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*([0-9a-f]+):\s+(\S+)\s*(.*)$", RegexOptions.CultureInvariant);
            if (!m.Success) continue;
            var address = Hex(m.Groups[1].Value);
            if (address < image.Base + (uint)function.Start || address >= image.Base + (uint)function.End)
                throw new InvalidDataException("Disassembler returned an address outside the requested function");
            instructions.Add(new((int)(address - image.Base), m.Groups[2].Value, m.Groups[3].Value.Trim()));
        }
        if (instructions.Count == 0 || instructions[0].Address != function.Start
            || !instructions.Select(i => i.Address).SequenceEqual(instructions.Select(i => i.Address).Distinct().Order()))
            throw new InvalidDataException("Disassembler returned invalid function boundaries");
        var result = instructions.ToArray();
        cache.Add(function.Start, result);
        return result;
    }

    internal int? DirectTarget(string operand)
    {
        var m = Regex.Match(operand, @"^0x([0-9a-f]+)(?:\s|$)", RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        var address = Hex(m.Groups[1].Value);
        return address >= image.Base && address - image.Base < image.Size ? (int)(address - image.Base) : null;
    }

    internal int? RipTarget(string operand)
    {
        var m = Regex.Match(operand, @"#\s*0x([0-9a-f]+)(?:\s|$)", RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        var address = Hex(m.Groups[1].Value);
        return address >= image.Base && address - image.Base < image.Size ? (int)(address - image.Base) : null;
    }

    private static ulong Hex(string value) => ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string Run(string tool, string[] arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000))
        {
            p.Kill();
            throw new InvalidDataException("External disassembler exceeded its time budget");
        }
        if (p.ExitCode != 0) throw new InvalidDataException($"{Path.GetFileName(tool)} failed: {error.Result}");
        if (output.Result.Length > 8 * 1024 * 1024) throw new InvalidDataException("Disassembler output exceeds its budget");
        return output.Result;
    }
}
