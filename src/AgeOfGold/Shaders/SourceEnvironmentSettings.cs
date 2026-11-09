using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using AgeOfGold.Runtime;

namespace AgeOfGold.Shaders;

internal sealed record SourceEnvironmentSettings(int LightingType, float Strength)
{
    internal static SourceEnvironmentSettings Read(Install source, string toolchain)
    {
        var image = new ExecutableImage(source.Read("DarkSoulsRemastered.exe"));
        SourceRendererSettings.Read(image);
        var normal = SourceRendererSettings.Storage(image, "m_dbgNormalStrength", 2);
        var flag = SourceRendererSettings.Storage(image, "m_dbgGIEnvLightMode", 3);
        var decode = new NativeDisassembler(toolchain, source.Locate("DarkSoulsRemastered.exe"), image);
        var functions = image.RelativeReferences([normal]).Select(image.Containing).OfType<ExecutableImage.Function>().Distinct();
        var matches = new List<SourceEnvironmentSettings>();
        foreach (var f in functions)
        {
            var code = decode.Read(f);
            for (var gate = 3; gate + 2 < code.Length; gate++)
            {
                // Ordinary environment lighting is enabled by either the material mode or the named flag.
                if (code[gate].Op != "cmp" || decode.RipTarget(code[gate].Operand) != flag
                    || !Plain(code[gate]).StartsWith("byte ptr [rip", StringComparison.Ordinal)
                    || !Plain(code[gate]).EndsWith(", 0x0", StringComparison.Ordinal)
                    || code[gate - 3].Op != "call" || decode.DirectTarget(code[gate - 3].Operand) is not int getter
                    || !image.Has(getter, 1, SectionCharacteristics.MemExecute)
                    || code[gate - 2].Op != "cmp" || code[gate - 1].Op != "je"
                    || decode.DirectTarget(code[gate - 1].Operand) != code[gate + 2].Address
                    || code[gate + 1].Op != "je" || decode.DirectTarget(code[gate + 1].Operand) is not int end
                    || end <= code[gate + 2].Address || !code.Any(i => i.Address == end)) continue;
                var mode = Regex.Match(Plain(code[gate - 2]), @"^eax, 0x([0-9a-f]+)$", RegexOptions.CultureInvariant);
                if (!mode.Success || !int.TryParse(mode.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var kind) || kind < 0) continue;
                var loads = Enumerable.Range(gate + 2, code.Length - gate - 2)
                    .Where(i => code[i].Address < end && code[i].Op == "movss" && decode.RipTarget(code[i].Operand) == normal).ToArray();
                if (loads.Length != 1) continue;
                var gain = PackedGain(code, loads[0], end, normal, decode, image);
                if (gain is not int address) continue;
                if (!image.Has(address, 4, SectionCharacteristics.MemRead, SectionCharacteristics.MemExecute | SectionCharacteristics.MemWrite))
                    throw new InvalidDataException("Unsupported source environment gain storage");
                var value = BitConverter.UInt32BitsToSingle(image.U32(image.Raw(address, 4)));
                if (!float.IsFinite(value) || value < 0) throw new InvalidDataException("Invalid source environment gain");
                matches.Add(new(kind, value));
            }
        }
        if (matches.Count != 1) throw new InvalidDataException($"Expected one source environment parameter path; found {matches.Count}");
        return matches[0];
    }

    private static int? PackedGain(NativeDisassembler.Instruction[] code, int start, int end, int normal,
        NativeDisassembler decode, ExecutableImage image)
    {
        const int unknown = -1, zero = -2;
        var registers = new Dictionary<string, int[]>();
        int[] Read(string name) => registers.GetValueOrDefault(name) ?? [unknown, unknown, unknown, unknown];
        // lazy: one straight-line scalar pack; extend only after another renderer's construction is verified.
        for (var at = start; at < Math.Min(start + 16, code.Length) && code[at].Address < end; at++)
        {
            var instruction = code[at];
            var operands = Plain(instruction).Split(", ", StringSplitOptions.TrimEntries);
            if (operands.Length != 2) return null;
            var destination = operands[0]; var source = operands[1];
            if (instruction.Op == "movss" && Register(destination) && decode.RipTarget(instruction.Operand) is int address)
                registers[destination] = [address, zero, zero, zero];
            else if (instruction.Op == "xorps" && Register(destination) && destination == source)
                registers[destination] = [zero, zero, zero, zero];
            else if (instruction.Op == "unpcklps" && Register(destination) && Register(source))
            {
                var a = Read(destination); var b = Read(source);
                registers[destination] = [a[0], b[0], a[1], b[1]];
            }
            else if (instruction.Op == "movaps" && destination.StartsWith("xmmword ptr [", StringComparison.Ordinal) && Register(source))
            {
                var vector = Read(source);
                if (vector[0] < 0 || vector[0] == normal || vector[1] != zero || vector[2] != normal) return null;
                var storage = destination[12..];
                if (!Regex.IsMatch(storage, @"^\[(?:rsp|rbp)(?: [+-] 0x[0-9a-f]+)?\]$", RegexOptions.CultureInvariant)) return null;
                var passed = false;
                for (var next = at + 1; next < Math.Min(at + 6, code.Length) && code[next].Address < end; next++)
                {
                    var i = code[next]; var text = Plain(i);
                    if (i.Op == "lea" && text == "r8, " + storage) passed = true;
                    else if (i.Op == "call") return passed && decode.DirectTarget(i.Operand) is int target
                        && image.Has(target, 1, SectionCharacteristics.MemExecute) ? vector[0] : null;
                    else if (i.Op != "mov" || !Regex.IsMatch(text, @"^r(?:ax|bx|cx|dx|si|di|9|1[0-5]), ", RegexOptions.CultureInvariant)) return null;
                }
                return null;
            }
            else return null;
        }
        return null;
    }

    private static string Plain(NativeDisassembler.Instruction instruction) => instruction.Operand.Split('#')[0].Trim();
    private static bool Register(string value) => Regex.IsMatch(value, @"^xmm(?:[0-9]|1[0-5])$", RegexOptions.CultureInvariant);
}
