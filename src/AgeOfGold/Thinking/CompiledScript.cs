using System.Buffers.Binary;
using System.Text;

namespace AgeOfGold.Thinking;

/// <summary>
/// A compiled script of the script language both games write their AI in. Both compile with the same release of the
/// language into the same layout, so a script is read whole, changed only in the globals it names, and written back.
/// </summary>
internal sealed class CompiledScript
{
    /// <summary>One function of a script: its instructions, the constants and functions they name, and what the compiler kept for error messages.</summary>
    internal sealed class Function
    {
        internal string? Source; internal int Line; internal byte Upvalues, Parameters, Vararg, Stack;
        internal int[] Lines = []; internal List<(string? Name, int From, int To)> Locals = []; internal List<string?> UpvalueNames = [];
        /// <summary>Numbers, text and nothing.</summary>
        internal List<object?> Constants = [];
        internal List<Function> Protos = [];
        internal uint[] Code = [];

        internal IEnumerable<Function> All() => new[] { this }.Concat(Protos.SelectMany(p => p.All()));
    }

    // An instruction keeps its kind in its low six bits, a register in its high eight, and a wide number between. The
    // kinds this tool reads or writes: reading and setting a global by a constant's number, loading a constant, making
    // a function, calling and returning, and building a table.
    internal const int Move = 0, LoadConstant = 1, ReadGlobal = 5, SetGlobal = 7, SetEntry = 9, NewTable = 10, Call = 25, TailCall = 26, Return = 27, MakeFunction = 34;
    /// <summary>A narrow number from here up names a constant rather than a register.</summary>
    internal const int ConstantFrom = 250;
    internal static uint Wide(int kind, int register, int wide) => (uint)(kind | wide << 6 | register << 24);
    internal static uint Narrow(int kind, int register, int first, int second) => (uint)(kind | second << 6 | first << 15 | register << 24);
    private static int Kind(uint instruction) => (int)(instruction & 63);
    private static int Register(uint instruction) => (int)(instruction >> 24);
    private static int WideOf(uint instruction) => (int)(instruction >> 6 & 0x3ffff);

    // The fixed opening: the language's mark and release, the sizes of its numbers and of an instruction's parts, and a
    // test number, all as both games' compilers write them.
    private static readonly byte[] Opening = [0x1b, 0x4c, 0x75, 0x61, 0x50, 1, 4, 8, 4, 6, 8, 9, 9, 8, 0xb6, 0x09, 0x93, 0x68, 0xe7, 0xf5, 0x7d, 0x41];

    internal Function Main { get; }

    internal CompiledScript(Function main) => Main = main;

    internal static CompiledScript Read(byte[] bytes)
    {
        if (bytes.Length < Opening.Length || !bytes.AsSpan(0, Opening.Length).SequenceEqual(Opening)) throw new InvalidDataException("Not a compiled script of the release and layout both games use");
        int at = Opening.Length;
        ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || at > bytes.Length - count) throw new InvalidDataException("Compiled script is shorter than it says");
            at += count; return bytes.AsSpan(at - count, count);
        }
        int Whole() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        int Count() => Whole() is >= 0 and var count ? count : throw new InvalidDataException("Compiled script counts below nothing");
        // Text is counted with its closing nought, in eight bytes; a count of nothing is no text at all.
        string? Text()
        {
            long length = BinaryPrimitives.ReadInt64LittleEndian(Take(8));
            if (length == 0) return null;
            if (length < 0 || length > bytes.Length) throw new InvalidDataException("Compiled script holds text longer than itself");
            var text = Take((int)length);
            return text[^1] == 0 ? Encoding.Latin1.GetString(text[..^1]) : throw new InvalidDataException("Compiled script's text is not closed");
        }
        Function One()
        {
            var function = new Function { Source = Text(), Line = Whole() };
            var sizes = Take(4); function.Upvalues = sizes[0]; function.Parameters = sizes[1]; function.Vararg = sizes[2]; function.Stack = sizes[3];
            function.Lines = new int[Count()]; for (int i = 0; i < function.Lines.Length; i++) function.Lines[i] = Whole();
            for (int n = Count(); n > 0; n--) { string? name = Text(); int from = Whole(), to = Whole(); function.Locals.Add((name, from, to)); }
            for (int n = Count(); n > 0; n--) function.UpvalueNames.Add(Text());
            for (int n = Count(); n > 0; n--)
                function.Constants.Add(Take(1)[0] switch
                {
                    3 => BinaryPrimitives.ReadDoubleLittleEndian(Take(8)),
                    4 => Text() ?? throw new InvalidDataException("Compiled script holds a text constant with no text"),
                    0 => null,
                    _ => throw new InvalidDataException("Unknown kind of constant in a compiled script"),
                });
            for (int n = Count(); n > 0; n--) function.Protos.Add(One());
            function.Code = new uint[Count()];
            for (int i = 0; i < function.Code.Length; i++) function.Code[i] = BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
            // What names a constant or a function within must name one that is there.
            foreach (uint instruction in function.Code)
                if (Kind(instruction) is ReadGlobal or SetGlobal && (WideOf(instruction) >= function.Constants.Count || function.Constants[WideOf(instruction)] is not string)
                    || Kind(instruction) == LoadConstant && WideOf(instruction) >= function.Constants.Count || Kind(instruction) == MakeFunction && WideOf(instruction) >= function.Protos.Count)
                    throw new InvalidDataException("Compiled script names a constant or a function it does not hold");
            return function;
        }
        var main = One();
        return at == bytes.Length ? new(main) : throw new InvalidDataException("Compiled script has bytes after its end");
    }

    internal byte[] Write()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Text(string? text)
        {
            if (text is null) { writer.Write(0L); return; }
            writer.Write((long)text.Length + 1); writer.Write(Encoding.Latin1.GetBytes(text)); writer.Write((byte)0);
        }
        void One(Function function)
        {
            Text(function.Source); writer.Write(function.Line); writer.Write(function.Upvalues); writer.Write(function.Parameters); writer.Write(function.Vararg); writer.Write(function.Stack);
            writer.Write(function.Lines.Length); foreach (int line in function.Lines) writer.Write(line);
            writer.Write(function.Locals.Count); foreach (var (name, from, to) in function.Locals) { Text(name); writer.Write(from); writer.Write(to); }
            writer.Write(function.UpvalueNames.Count); foreach (var name in function.UpvalueNames) Text(name);
            writer.Write(function.Constants.Count);
            foreach (var constant in function.Constants)
                switch (constant)
                {
                    case double number: writer.Write((byte)3); writer.Write(number); break;
                    case string text: writer.Write((byte)4); Text(text); break;
                    case null: writer.Write((byte)0); break;
                    default: throw new InvalidDataException("A compiled script's constants are numbers, text and nothing");
                }
            writer.Write(function.Protos.Count); foreach (var within in function.Protos) One(within);
            writer.Write(function.Code.Length); foreach (uint instruction in function.Code) writer.Write(instruction);
        }
        writer.Write(Opening); One(Main);
        return stream.ToArray();
    }

    private IEnumerable<string> Named(int kind) => Main.All().SelectMany(f => f.Code.Where(i => Kind(i) == kind).Select(i => (string)f.Constants[WideOf(i)]!)).Distinct(StringComparer.Ordinal);

    /// <summary>The globals the script reads, anywhere in it.</summary>
    internal IEnumerable<string> Reads() => Named(ReadGlobal);

    /// <summary>The globals the script sets, anywhere in it.</summary>
    internal IEnumerable<string> Writes() => Named(SetGlobal);

    /// <summary>Moves every read and write of a global whose name the map changes to the new name.</summary>
    internal void Rename(Func<string, string?> map)
    {
        foreach (var function in Main.All())
            for (int i = 0; i < function.Code.Length; i++)
            {
                uint instruction = function.Code[i];
                if (Kind(instruction) is not (ReadGlobal or SetGlobal) || map((string)function.Constants[WideOf(instruction)]!) is not { } renamed) continue;
                // The old name may also be used as plain text, so the new one gets a constant of its own.
                int at = function.Constants.IndexOf(renamed);
                if (at < 0) { at = function.Constants.Count; function.Constants.Add(renamed); }
                function.Code[i] = Wide(Kind(instruction), Register(instruction), at);
            }
    }

    /// <summary>The globals the script's top level sets straight from a number, in order.</summary>
    internal IReadOnlyList<KeyValuePair<string, double>> Numbers()
    {
        var found = new List<KeyValuePair<string, double>>();
        for (int i = 1; i < Main.Code.Length; i++)
        {
            uint set = Main.Code[i], from = Main.Code[i - 1];
            if (Kind(set) == SetGlobal && Kind(from) == LoadConstant && Register(set) == Register(from) && Main.Constants[WideOf(from)] is double number)
                found.Add(new((string)Main.Constants[WideOf(set)]!, number));
        }
        return found;
    }

    /// <summary>The globals the script's top level sets to functions of its own, in order.</summary>
    internal IReadOnlyList<string> Functions()
    {
        var found = new List<string>();
        for (int i = 0; i < Main.Code.Length; i++)
        {
            uint made = Main.Code[i];
            if (Kind(made) != MakeFunction) continue;
            // After a made function comes one instruction for each value it encloses; the global is set after those.
            int set = i + 1 + Main.Protos[WideOf(made)].Upvalues;
            if (set < Main.Code.Length && Kind(Main.Code[set]) == SetGlobal && Register(Main.Code[set]) == Register(made)) found.Add((string)Main.Constants[WideOf(Main.Code[set])]!);
        }
        return found;
    }
}
