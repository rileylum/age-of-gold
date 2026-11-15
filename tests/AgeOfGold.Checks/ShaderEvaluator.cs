using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Evaluates a scalar shader-model 6.0 program on the CPU: straight arithmetic, branches, inputs, constants and
/// texture samples supplied by the caller. It gives numerical evidence where no GPU run is available.
/// </summary>
internal sealed class ShaderEvaluator
{
    internal Func<int, int, object> Input = (_, _) => throw new Exception("Unexpected shader input");
    internal Func<int, int, float[]> Constants = (_, _) => throw new Exception("Unexpected constant read");
    /// <summary>Texture range, coordinates (two for a surface, three for a cube) and whether a level was explicit.</summary>
    internal Func<int, float[], bool, float[]> Sample = (_, _, _) => throw new Exception("Unexpected texture sample");
    internal readonly Dictionary<(int Id, int Column), float> Outputs = [];
    /// <summary>Whether the program discarded the pixel; evaluation continues so its outputs can still be compared.</summary>
    internal bool Discarded { get; private set; }

    internal void Run(string module)
    {
        var lines = module.Replace("\r\n", "\n").Split('\n').Select(l => l.Split(';')[0].TrimEnd()).ToArray();
        int start = Array.FindIndex(lines, l => l.StartsWith("define void @", StringComparison.Ordinal));
        int end = Array.FindIndex(lines, start + 1, l => l == "}");
        var labels = new Dictionary<string, int> { ["0"] = start + 1 };
        // The disassembler writes block labels as comments; recover them from the unstripped text.
        var raw = module.Replace("\r\n", "\n").Split('\n');
        for (int i = start + 1; i < end; i++)
        {
            var label = Regex.Match(raw[i], @"^; <label>:(\d+)");
            if (label.Success) labels[label.Groups[1].Value] = i + 1;
            var named = Regex.Match(raw[i], @"^([\w.]+):");
            if (named.Success) labels[named.Groups[1].Value] = i + 1;
        }
        var values = new Dictionary<string, object>();
        string block = "0", previous = "";
        int at = start + 1, steps = 0;
        object Value(string token)
        {
            token = token.Trim();
            if (token.StartsWith('%')) return values.TryGetValue(token, out var v) ? v : throw new Exception("Undefined shader value " + token);
            if (token is "undef") return 0f;
            if (token is "true") return true;
            if (token is "false") return false;
            if (token.StartsWith("0x", StringComparison.Ordinal))
                return (float)BitConverter.Int64BitsToDouble(long.Parse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            if (token.Contains('.') || token.Contains('e')) return float.Parse(token, CultureInfo.InvariantCulture);
            return int.Parse(token, CultureInfo.InvariantCulture);
        }
        float F(string token) => Convert.ToSingle(Value(Strip(token)), CultureInfo.InvariantCulture);
        int I(string token) => Value(Strip(token)) is bool b ? (b ? 1 : 0) : Convert.ToInt32(Value(Strip(token)), CultureInfo.InvariantCulture);
        static string Strip(string token) => Regex.Replace(token.Trim(), @"^(float|i32|i8|i1|%dx\.types\.\w+(\.\w+)?) ", "");
        static string[] Arguments(string text)
        {
            int open = text.IndexOf('('), close = text.LastIndexOf(')');
            return text[(open + 1)..close].Split(',').Select(a => a.Trim()).ToArray();
        }
        while (true)
        {
            if (++steps > 100000) throw new Exception("Shader evaluation did not terminate");
            string line = lines[at++].Trim();
            if (line.Length == 0) continue;
            if (line == "ret void") return;
            var branch = Regex.Match(line, @"^br i1 (%[\w.]+), label %([\w.]+), label %([\w.]+)$");
            var jump = Regex.Match(line, @"^br label %([\w.]+)$");
            if (branch.Success || jump.Success)
            {
                string target = jump.Success ? jump.Groups[1].Value : (bool)Value(branch.Groups[1].Value) ? branch.Groups[2].Value : branch.Groups[3].Value;
                previous = block; block = target; at = labels[target];
                continue;
            }
            var assign = Regex.Match(line, @"^(%[\w.$-]+) = (.+)$");
            string text = assign.Success ? assign.Groups[2].Value : line;
            object? result = null;
            if (text.StartsWith("call ", StringComparison.Ordinal))
            {
                var a = Arguments(text);
                string name = Regex.Match(text, @"@dx\.op\.(\w+)").Groups[1].Value;
                int code = I(a[0]);
                result = (name, code) switch
                {
                    ("discard", 82) => Discard((bool)Value(Strip(a[1]))),
                    ("createHandle", 57) => (object)(I(a[1]), I(a[3])),
                    ("loadInput", 4) => Input(I(a[1]), I(a[3])),
                    ("storeOutput", 5) => Store(I(a[1]), I(a[3]), F(a[4])),
                    ("cbufferLoadLegacy", 59) => Constants((((int, int))Value(Strip(a[1]))).Item2, I(a[2])),
                    // A load reads one texel; the check's textures are the same everywhere.
                    ("textureLoad", 66) => Sample((((int, int))Value(Strip(a[1]))).Item2, [], true),
                    ("sample", 60) => Sample((((int, int))Value(Strip(a[1]))).Item2, [F(a[3]), F(a[4])], false),
                    ("sampleLevel", 62) => Sample((((int, int))Value(Strip(a[1]))).Item2, a[5].EndsWith("undef", StringComparison.Ordinal) ? [F(a[3]), F(a[4])] : [F(a[3]), F(a[4]), F(a[5])], true),
                    ("unary", 6) => MathF.Abs(F(a[1])),
                    ("unary", 7) => Math.Clamp(F(a[1]), 0f, 1f),
                    ("unary", 21) => MathF.Pow(2, F(a[1])),
                    ("unary", 23) => MathF.Log2(F(a[1])),
                    ("unary", 24) => MathF.Sqrt(F(a[1])),
                    ("unary", 25) => 1 / MathF.Sqrt(F(a[1])),
                    ("unary", 26) => MathF.Round(F(a[1]), MidpointRounding.ToEven),
                    ("binary", 35) => MathF.Max(F(a[1]), F(a[2])),
                    ("binary", 36) => MathF.Min(F(a[1]), F(a[2])),
                    ("tertiary", 46) => F(a[1]) * F(a[2]) + F(a[3]),
                    ("dot2", 54) => F(a[1]) * F(a[3]) + F(a[2]) * F(a[4]),
                    ("dot4", 56) => F(a[1]) * F(a[5]) + F(a[2]) * F(a[6]) + F(a[3]) * F(a[7]) + F(a[4]) * F(a[8]),
                    ("dot3", 55) => F(a[1]) * F(a[4]) + F(a[2]) * F(a[5]) + F(a[3]) * F(a[6]),
                    _ => throw new Exception("Unsupported shader operation in evaluator: " + name + " " + code),
                };
            }
            else
            {
                var arithmetic = Regex.Match(text, @"^(fadd|fsub|fmul|fdiv)(?: fast)? float ([^,]+), (.+)$");
                var integer = Regex.Match(text, @"^(add|sub|mul|and|or|shl|lshr)(?: nuw| nsw)* i32 ([^,]+), (.+)$");
                var extract = Regex.Match(text, @"^extractvalue %dx\.types\.\w+\.\w+ (%[\w.$-]+), (\d+)$");
                var compare = Regex.Match(text, @"^fcmp(?: fast)? (\w+) float ([^,]+), (.+)$");
                var integerCompare = Regex.Match(text, @"^icmp (\w+) i32 ([^,]+), (.+)$");
                var select = Regex.Match(text, @"^select i1 ([^,]+), (?:float|i32) ([^,]+), (?:float|i32) (.+)$");
                var phi = Regex.Match(text, @"^phi (?:float|i32) (.+)$");
                var convert = Regex.Match(text, @"^(fptoui|fptosi|uitofp|sitofp) (?:float|i32) (\S+) to (?:float|i32)$");
                if (arithmetic.Success)
                {
                    float x = F(arithmetic.Groups[2].Value), y = F(arithmetic.Groups[3].Value);
                    result = arithmetic.Groups[1].Value switch { "fadd" => x + y, "fsub" => x - y, "fmul" => x * y, _ => x / y };
                }
                else if (integer.Success)
                {
                    int x = I(integer.Groups[2].Value), y = I(integer.Groups[3].Value);
                    result = integer.Groups[1].Value switch { "add" => x + y, "sub" => x - y, "mul" => x * y, "and" => x & y, "or" => x | y, "shl" => x << y, _ => (int)((uint)x >> y) };
                }
                else if (extract.Success)
                {
                    var source = Value(extract.Groups[1].Value);
                    result = source is float[] floats ? floats[int.Parse(extract.Groups[2].Value)] : throw new Exception("Unsupported aggregate in evaluator");
                }
                else if (compare.Success)
                {
                    float x = F(compare.Groups[2].Value), y = F(compare.Groups[3].Value);
                    result = compare.Groups[1].Value switch { "une" or "one" => x != y, "oeq" or "ueq" => x == y, "olt" or "ult" => x < y, "ogt" or "ugt" => x > y, "ole" or "ule" => x <= y, "oge" or "uge" => x >= y, _ => throw new Exception("Unsupported comparison") };
                }
                else if (integerCompare.Success)
                {
                    int x = I(integerCompare.Groups[2].Value), y = I(integerCompare.Groups[3].Value);
                    result = integerCompare.Groups[1].Value switch { "eq" => x == y, "ne" => x != y, "slt" => x < y, "sgt" => x > y, "ult" => (uint)x < (uint)y, "ugt" => (uint)x > (uint)y, _ => throw new Exception("Unsupported comparison") };
                }
                else if (select.Success) result = (bool)Value(select.Groups[1].Value) ? Value(select.Groups[2].Value) : Value(select.Groups[3].Value);
                else if (phi.Success)
                {
                    var arms = Regex.Matches(phi.Groups[1].Value, @"\[ ([^,]+), %([\w.]+) \]");
                    var arm = arms.FirstOrDefault(m => m.Groups[2].Value == previous) ?? throw new Exception("Shader phi lacks its predecessor");
                    result = Value(arm.Groups[1].Value);
                }
                else if (convert.Success)
                    result = convert.Groups[1].Value is "fptoui" or "fptosi" ? (int)F(convert.Groups[2].Value) : (object)(float)I(convert.Groups[2].Value);
                else throw new Exception("Unsupported shader instruction in evaluator: " + text);
            }
            if (assign.Success) values[assign.Groups[1].Value] = result!;
        }
    }

    private object Discard(bool condition) { Discarded |= condition; return 0; }
    private object Store(int id, int column, float value) { Outputs[(id, column)] = value; return 0; }
}
