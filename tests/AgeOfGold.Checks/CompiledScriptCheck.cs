using System.Text;
using AgeOfGold.Thinking;

internal static class CompiledScriptCheck
{
    // An instruction: a kind in its low six bits and a register in its high eight, with either one wide number or two
    // narrow ones between.
    private static uint Wide(int kind, int register, int wide) => (uint)(kind | wide << 6 | register << 24);
    private static uint Narrow(int kind, int register, int first, int second) => (uint)(kind | second << 6 | first << 15 | register << 24);
    private const int Move = 0, LoadConstant = 1, ReadGlobal = 5, SetGlobal = 7, SetEntry = 9, NewTable = 10, Call = 25, Return = 27, MakeFunction = 34;
    // A narrow number from here up names a constant rather than a register.
    private const int ConstantFrom = 250;

    internal static void Run()
    {
        // A whole compiled script written out by hand: the fixed opening, then one function that sets the global LIMIT
        // to 3.5 and returns. Lengths of text count a closing nought and take eight bytes; other counts take four.
        byte[] byHand =
        [
            0x1b, (byte)'L', (byte)'u', (byte)'a', 0x50, 1, 4, 8, 4, 6, 8, 9, 9, 8, 0xb6, 0x09, 0x93, 0x68, 0xe7, 0xf5, 0x7d, 0x41,
            // Named "@a", defined at line 0; no enclosed values, no parameters, not open-ended, two registers.
            3, 0, 0, 0, 0, 0, 0, 0, (byte)'@', (byte)'a', 0, 0, 0, 0, 0, 0, 0, 0, 2,
            // One line number a instruction, here none kept; no named locals; no enclosed names.
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // Two constants: the number 3.5 and the text "LIMIT".
            2, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0x0c, 0x40, 4, 6, 0, 0, 0, 0, 0, 0, 0, (byte)'L', (byte)'I', (byte)'M', (byte)'I', (byte)'T', 0,
            // No functions within; three instructions: load constant 0 into register 0, set global (constant 1) from it, return.
            0, 0, 0, 0, 3, 0, 0, 0,
            0x01, 0, 0, 0, 0x47, 0, 0, 0, 0x1b, 0x80, 0, 0,
        ];
        var read = CompiledScript.Read(byHand);
        if (read.Main.Source != "@a" || read.Main.Stack != 2 || read.Main.Code.Length != 3 || !read.Numbers().SequenceEqual([new("LIMIT", 3.5)]) || !read.Writes().SequenceEqual(["LIMIT"]) || read.Reads().Any())
            throw new Exception("A compiled script written by hand is read otherwise");
        if (!read.Write().SequenceEqual(byHand)) throw new Exception("A compiled script is not written back as it was read");
        foreach (var bad in new[] { byHand[..^1], [.. byHand, 0], [.. byHand[..4], 0x51, .. byHand[5..]], [.. byHand[..57], 9, .. byHand[58..]] })
        {
            try { CompiledScript.Read(bad); throw new Exception("An unusable compiled script was accepted"); }
            catch (InvalidDataException) { }
        }

        // A script that reads and sets a global, and also holds the global's name as plain text: renaming moves the
        // reading and the setting to the new name and leaves the text.
        var renamed = new CompiledScript(new CompiledScript.Function
        {
            Stack = 2, Constants = ["Helper"],
            Code = [Wide(ReadGlobal, 0, 0), Wide(SetGlobal, 0, 0), Wide(LoadConstant, 1, 0), Narrow(Return, 0, 1, 0)],
        });
        renamed.Rename(name => name == "Helper" ? "own_Helper" : null);
        var after = CompiledScript.Read(renamed.Write());
        if (!after.Reads().SequenceEqual(["own_Helper"]) || !after.Writes().SequenceEqual(["own_Helper"]) || after.Main.Constants[(int)(after.Main.Code[2] >> 6 & 0x3ffff)] as string != "Helper")
            throw new Exception("Renaming a global does not move its reads and writes alone");

        // A script's top level names the functions it defines: a made function, then one instruction for each value it
        // encloses, then the global set from it.
        var defining = new CompiledScript(new CompiledScript.Function
        {
            Stack = 2, Constants = ["Plain", "Enclosing", "COUNT", 4.0],
            Protos = [new() { Code = [Narrow(Return, 0, 1, 0)] }, new() { Upvalues = 1, Code = [Narrow(Return, 0, 1, 0)] }],
            Code = [Wide(MakeFunction, 0, 0), Wide(SetGlobal, 0, 0), Wide(MakeFunction, 0, 1), Narrow(Move, 0, 1, 0), Wide(SetGlobal, 0, 1), Wide(LoadConstant, 0, 3), Wide(SetGlobal, 0, 2), Narrow(Return, 0, 1, 0)],
        });
        if (!defining.Functions().SequenceEqual(["Plain", "Enclosing"]) || !defining.Numbers().SequenceEqual([new("COUNT", 4.0)])) throw new Exception("A script's own functions and numbers are listed otherwise");
        Console.WriteLine("PASS: a compiled script is read, written back byte for byte, renamed by global, and lists what its top level defines; other layouts fail");

        // A source battle script, as its compiler would leave it. Its top level defines the goal's parts as plain
        // functions and registers the goal with the engine by number and prefix. Activating calls a shared function of the
        // source's; updating returns a number the host defines.
        CompiledScript.Function Part(params object[] body) => new() { Parameters = 2, Stack = 6, Constants = body.OfType<string>().Cast<object?>().ToList(), Code = body.OfType<uint>().ToArray() };
        var battle = new CompiledScript(new CompiledScript.Function
        {
            Source = "@authored battle", Stack = 4,
            Constants = ["Fight77Battle_Activate", "Fight77Battle_Update", "Fight77Battle_Interupt", "REGISTER_GOAL", "GOAL_Fight77_Battle", "Fight77"],
            Protos =
            [
                // Activate: Shared_Act(character, goal).
                Part("Shared_Act", Wide(ReadGlobal, 2, 0), Narrow(Move, 3, 0, 0), Narrow(Move, 4, 1, 0), Narrow(Call, 2, 3, 1), Narrow(Return, 0, 1, 0)),
                // Update: return GOAL_RESULT_Continue.
                Part("GOAL_RESULT_Continue", Wide(ReadGlobal, 2, 0), Narrow(Return, 2, 2, 0), Narrow(Return, 0, 1, 0)),
                // Interrupt: return the goal it was given.
                Part(Narrow(Return, 1, 2, 0), Narrow(Return, 0, 1, 0)),
            ],
            Code =
            [
                Wide(MakeFunction, 0, 0), Wide(SetGlobal, 0, 0), Wide(MakeFunction, 0, 1), Wide(SetGlobal, 0, 1), Wide(MakeFunction, 0, 2), Wide(SetGlobal, 0, 2),
                Wide(ReadGlobal, 0, 3), Wide(ReadGlobal, 1, 4), Wide(LoadConstant, 2, 5), Narrow(Call, 0, 3, 1), Narrow(Return, 0, 1, 0),
            ],
        });
        // The source's shared scripts. One defines the shared function, which calls an engine function with a number of
        // the script's own; the host has a function of the same name that does otherwise. Another is not reached. A
        // third only numbers things, the goal among them.
        var shared = new CompiledScript(new CompiledScript.Function
        {
            Source = "@authored shared", Stack = 2, Constants = ["Shared_Act", "SHARED_MARK", 8.0],
            Protos = [Part("Record", "SHARED_MARK", Wide(ReadGlobal, 2, 0), Narrow(Move, 3, 0, 0), Narrow(Move, 4, 1, 0), Wide(ReadGlobal, 5, 1), Narrow(Call, 2, 4, 1), Narrow(Return, 0, 1, 0))],
            Code = [Wide(LoadConstant, 0, 2), Wide(SetGlobal, 0, 1), Wide(MakeFunction, 0, 0), Wide(SetGlobal, 0, 0), Narrow(Return, 0, 1, 0)],
        });
        var unreached = new CompiledScript(new CompiledScript.Function
        {
            Stack = 2, Constants = ["Unreached"], Protos = [Part(Narrow(Return, 0, 1, 0))], Code = [Wide(MakeFunction, 0, 0), Wide(SetGlobal, 0, 0), Narrow(Return, 0, 1, 0)],
        });
        var numbering = new CompiledScript(new CompiledScript.Function
        {
            Stack = 2, Constants = ["GOAL_Fight77_Battle", 77.0, "GOAL_RESULT_Continue", 99.0],
            Code = [Wide(LoadConstant, 0, 1), Wide(SetGlobal, 0, 0), Wide(LoadConstant, 0, 3), Wide(SetGlobal, 0, 2), Narrow(Return, 0, 1, 0)],
        });
        var packaged = HostBattleScript.Package(77, battle.Write(), [("b_shared.lua", shared.Write()), ("a_unreached.lua", unreached.Write()), ("c_numbers.lua", numbering.Write())],
            new HashSet<string> { "GOAL_RESULT_Continue", "Shared_Act", "g_GoalTable" });
        if (!packaged.Carried.SequenceEqual(["b_shared.lua"]) || !packaged.Unmet.SequenceEqual(["REGISTER_GOAL", "Record"])
            || !packaged.Defines.Order(StringComparer.Ordinal).SequenceEqual(new[] { "Fight77Battle_Activate", "Fight77Battle_Interupt", "Fight77Battle_Update", "GOAL_Fight77_Battle", "aog_SHARED_MARK", "aog_Shared_Act" }.Order(StringComparer.Ordinal)))
            throw new Exception($"A packaged battle script carries {string.Join(",", packaged.Carried)}, leaves {string.Join(",", packaged.Unmet)} and defines {string.Join(",", packaged.Defines)}");

        // Run as the host would run it: with the host's own globals, the engine's functions, and its table of goals.
        var goals = new Dictionary<object, object?>(); var calls = new List<string>();
        var globals = new Dictionary<string, object?>
        {
            ["g_GoalTable"] = goals, ["GOAL_RESULT_Continue"] = 5.0,
            ["Shared_Act"] = (Func<object?[], object?[]>)(_ => { calls.Add("the host's shared function"); return []; }),
            ["REGISTER_GOAL"] = (Func<object?[], object?[]>)(a => { calls.Add($"registered {a[0]} {a[1]}"); return []; }),
            ["Record"] = (Func<object?[], object?[]>)(a => { calls.Add($"recorded {a[0]} {a[1]} {a[2]}"); return []; }),
        };
        var hostsOwn = globals["Shared_Act"];
        Execute(CompiledScript.Read(packaged.Script).Main, globals, []);
        // The source's top level ran (it registered its goal under the source's number); the host's function of the shared
        // name is untouched, and the source's stands beside it under the build's prefix.
        if (!calls.SequenceEqual(["registered 77 Fight77"]) || !ReferenceEquals(globals["Shared_Act"], hostsOwn) || globals.GetValueOrDefault("aog_Shared_Act") is not Closure || globals.ContainsKey("Unreached")
            || globals.GetValueOrDefault("GOAL_Fight77_Battle") is not 77.0 || globals["GOAL_RESULT_Continue"] is not 5.0)
            throw new Exception("A packaged battle script's top level leaves the host's globals otherwise: " + string.Join("; ", calls));
        // The host's table of goals holds, under the goal's number, a table of the parts the script defines; each takes the
        // table, the character and the goal, and hands the last two to the source's plain function.
        if (goals.Count != 1 || goals.GetValueOrDefault(77.0) is not Dictionary<object, object?> goal || !goal.Keys.Cast<string>().Order(StringComparer.Ordinal).SequenceEqual(["Activate", "Interrupt", "Update"]))
            throw new Exception("The host's table of goals does not hold the source's goal by its parts");
        calls.Clear();
        object?[] Asked(string part) => Invoke(goal[part], [goal, "the character", "the goal"], globals);
        if (Asked("Activate").Length != 0 || !calls.SequenceEqual(["recorded the character the goal 8"])) throw new Exception("Activating the goal does not reach the source's shared function: " + string.Join("; ", calls));
        if (!Asked("Update").SequenceEqual(new object?[] { 5.0 }) || !Asked("Interrupt").SequenceEqual(new object?[] { "the goal" })) throw new Exception("A goal's part does not return what the source's function returns");

        // The list of names beside a script: where each name starts, a closing nought, then the names in two-byte characters.
        byte[] names = HostBattleScript.Names(["Ab", "C"]);
        byte[] expected = [24, 0, 0, 0, 0, 0, 0, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)'A', 0, (byte)'b', 0, 0, 0, (byte)'C', 0, 0, 0, .. new byte[14]];
        if (!names.SequenceEqual(expected)) throw new Exception("A list of names is laid out otherwise: " + Convert.ToHexString(names));
        // A global the battle script itself sets under a name the host's shared scripts define takes the build's prefix
        // too: the host's is left alone, and the goal's part still reaches the source's.
        var clashing = HostBattleScript.Package(77, battle.Write(), [("b_shared.lua", shared.Write()), ("c_numbers.lua", numbering.Write())], new HashSet<string> { "GOAL_RESULT_Continue", "g_GoalTable", "Fight77Battle_Update" });
        goals.Clear(); globals.Remove("Fight77Battle_Update"); globals.Remove("aog_Shared_Act");
        Execute(CompiledScript.Read(clashing.Script).Main, globals, []);
        if (globals.ContainsKey("Fight77Battle_Update") || globals.GetValueOrDefault("aog_Fight77Battle_Update") is not Closure || !clashing.Defines.Contains("aog_Fight77Battle_Update") || clashing.Defines.Contains("Fight77Battle_Update")
            || !Invoke(((Dictionary<object, object?>)goals[77.0]!)["Update"], [null, "the character", "the goal"], globals).SequenceEqual(new object?[] { 5.0 }))
            throw new Exception("A battle script's own global of a name the host defines is not kept apart from the host's");
        // A script that defines no battle goal is not a battle script.
        try { HostBattleScript.Package(77, unreached.Write(), [], new HashSet<string>()); throw new Exception("An unusable battle script was packaged"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: a source battle script is packaged with the source's shared scripts it reaches under names of the build's own, and the host's table of goals reaches its parts");
    }

    private sealed record Closure(CompiledScript.Function Function);

    private static object?[] Invoke(object? what, object?[] arguments, Dictionary<string, object?> globals) => what switch
    {
        Closure made => Execute(made.Function, globals, arguments),
        Func<object?[], object?[]> native => native(arguments),
        _ => throw new Exception("The check's interpreter was asked to call what is not a function"),
    };

    // The few instructions the authored and packaged scripts use, as the script language defines them.
    private static object?[] Execute(CompiledScript.Function function, Dictionary<string, object?> globals, object?[] arguments)
    {
        var registers = new object?[function.Stack + 8]; int top = 0;
        for (int i = 0; i < function.Parameters && i < arguments.Length; i++) registers[i] = arguments[i];
        object? Either(int narrow) => narrow >= ConstantFrom ? function.Constants[narrow - ConstantFrom] : registers[narrow];
        for (int at = 0; ; at++)
        {
            uint instruction = function.Code[at];
            int kind = (int)(instruction & 63), register = (int)(instruction >> 24), wide = (int)(instruction >> 6 & 0x3ffff), first = (int)(instruction >> 15 & 511), second = (int)(instruction >> 6 & 511);
            switch (kind)
            {
                case Move: registers[register] = registers[first]; break;
                case LoadConstant: registers[register] = function.Constants[wide]; break;
                case ReadGlobal: registers[register] = globals.GetValueOrDefault((string)function.Constants[wide]!); break;
                case SetGlobal: globals[(string)function.Constants[wide]!] = registers[register]; break;
                case NewTable: registers[register] = new Dictionary<object, object?>(); break;
                case SetEntry: ((Dictionary<object, object?>)registers[register]!)[Either(first)!] = Either(second); break;
                case MakeFunction:
                    if (function.Protos[wide].Upvalues != 0) throw new Exception("The check's interpreter does not enclose values");
                    registers[register] = new Closure(function.Protos[wide]); break;
                case Call:
                {
                    // One less than `first` arguments follow the function (nought: up to the top); one less than `second`
                    // results come back (nought: all of them, and the top is set past them).
                    int given = first == 0 ? top - register - 1 : first - 1;
                    var results = Invoke(registers[register], registers.AsSpan(register + 1, given).ToArray(), globals);
                    int wanted = second == 0 ? results.Length : second - 1;
                    for (int i = 0; i < wanted; i++) registers[register + i] = i < results.Length ? results[i] : null;
                    if (second == 0) top = register + results.Length;
                    break;
                }
                case Return: return registers.AsSpan(register, first == 0 ? top - register : first - 1).ToArray();
                default: throw new Exception("The check's interpreter does not know instruction kind " + kind);
            }
        }
    }
}
