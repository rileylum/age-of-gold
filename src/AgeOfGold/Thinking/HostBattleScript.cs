using System.Text;

namespace AgeOfGold.Thinking;

/// <summary>
/// A source enemy's own battle script made into the one script the host runs for an enemy's battle number.
/// <para>The source's script is carried as compiled. It leans on the source's shared scripts, whose helpers the host
/// also has under the same names with other arguments and behaviour; so each shared script it reaches is carried whole
/// too, with every global that script sets under a name of the build's own, and the host's are neither used nor
/// disturbed. A name the host defines and the source's shared functions do not is the host's: it numbers something of
/// the host's engine.</para>
/// <para>The source defines a goal as plain functions named by a prefix; the host runs a goal through a table of
/// functions kept under the goal's number, each taking the table, the character and the goal. After the source's
/// script has run, such a table is put there, each entry handing the character and the goal to the source's function.</para>
/// </summary>
internal static class HostBattleScript
{
    /// <summary>The prefix the build's copies of the source's shared globals take.</summary>
    internal const string Own = "aog_";
    // The host's table of goals, and the parts of a goal as the host and the source name them (the source misspells one).
    private const string GoalTable = "g_GoalTable", BattleGoal = "Battle";
    private static readonly (string Host, string Source)[] Parts = [("Activate", "Activate"), ("Update", "Update"), ("Terminate", "Terminate"), ("Interrupt", "Interupt")];

    /// <param name="Script">The one compiled script for the host's binder.</param>
    /// <param name="Defines">Every global the script sets, for the list of names the host keeps beside it.</param>
    /// <param name="Carried">The source's shared scripts carried whole, by name.</param>
    /// <param name="Unmet">Globals the scripts read that neither game's scripts define: the engine's own functions, and names the source reads without ever setting.</param>
    internal sealed record Packaged(byte[] Script, string[] Defines, string[] Carried, string[] Unmet);

    /// <param name="number">The battle number: the host finds the script by it and keeps the goal under it.</param>
    /// <param name="battle">The source's compiled battle script.</param>
    /// <param name="sourceShared">The source's shared compiled scripts, by name.</param>
    /// <param name="hostDefined">The globals the host's own shared scripts set.</param>
    internal static Packaged Package(int number, byte[] battle, IReadOnlyList<(string Name, byte[] Script)> sourceShared, IReadOnlySet<string> hostDefined)
    {
        var script = CompiledScript.Read(battle);
        var own = script.Writes().ToHashSet(StringComparer.Ordinal);
        // The goal: the one prefix under which the script defines a battle's activation.
        var prefixes = script.Functions().Where(name => name.EndsWith(BattleGoal + "_Activate", StringComparison.Ordinal)).Select(name => name[..^"_Activate".Length]).ToArray();
        if (prefixes.Length != 1) throw new InvalidDataException($"Source battle script {number} does not define one battle goal");
        var parts = Parts.Where(part => script.Functions().Contains(prefixes[0] + "_" + part.Source)).ToArray();

        var shared = sourceShared.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s => (s.Name, Script: CompiledScript.Read(s.Script))).ToArray();
        var definedIn = new Dictionary<string, int>(StringComparer.Ordinal); var sourceNumbers = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int k = 0; k < shared.Length; k++)
        {
            foreach (string name in shared[k].Script.Functions()) definedIn.TryAdd(name, k);
            foreach (var (name, value) in shared[k].Script.Numbers()) sourceNumbers.TryAdd(name, value);
        }
        // Which shared scripts are reached: those defining a function the battle script reads, or one a reached script reads.
        var carried = new SortedSet<int>(); var read = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(script.Reads());
        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!read.Add(name) || !definedIn.TryGetValue(name, out int file) || !carried.Add(file)) continue;
            foreach (string within in shared[file].Script.Reads()) pending.Enqueue(within);
        }
        // What takes a name of the build's own: every global a carried shared script sets, and any the battle script
        // itself sets under a name the host's shared scripts define (the source's scripts each bring a helper or two of
        // their own that the host has under the same name).
        var mine = carried.SelectMany(k => shared[k].Script.Writes()).Concat(own.Where(hostDefined.Contains)).ToHashSet(StringComparer.Ordinal);
        // Of the rest that is read: the host's by name, else a number only the source sets, else nobody's.
        var numbers = new SortedDictionary<string, double>(StringComparer.Ordinal); var unmet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in read.Where(name => !own.Contains(name) && !mine.Contains(name) && !hostDefined.Contains(name)))
            if (sourceNumbers.TryGetValue(name, out double value)) numbers.Add(name, value); else unmet.Add(name);
        string Final(string name) => mine.Contains(name) ? Own + name : name;
        string[] defines = [.. numbers.Keys, .. mine.Select(Final).Order(StringComparer.Ordinal), .. own.Where(name => !mine.Contains(name)).Order(StringComparer.Ordinal)];
        if (defines.FirstOrDefault(hostDefined.Contains) is { } taken) throw new InvalidDataException($"Source battle script {number} would set a global the host's shared scripts define: {taken}");
        string? Renamed(string name) => mine.Contains(name) ? Own + name : null;
        foreach (int k in carried) shared[k].Script.Rename(Renamed);
        script.Rename(Renamed);

        // The one script: set the numbers; run each carried shared script's top level, then the battle script's; then
        // put the goal's table under its number in the host's table of goals.
        var main = new CompiledScript.Function { Source = "@age-of-gold", Stack = 4 };
        int Constant(object value) { int at = main.Constants.IndexOf(value); if (at < 0) { at = main.Constants.Count; main.Constants.Add(value); } return at; }
        // A table's key is named in an instruction's narrow number, which reaches only the first few constants: those come first.
        foreach (var part in parts) Constant(part.Host);
        Constant((double)number);
        if (main.Constants.Count > 511 - CompiledScript.ConstantFrom) throw new InvalidDataException("A goal has too many parts to name in one script");
        var code = new List<uint>();
        foreach (var (name, value) in numbers) { code.Add(CompiledScript.Wide(CompiledScript.LoadConstant, 0, Constant(value))); code.Add(CompiledScript.Wide(CompiledScript.SetGlobal, 0, Constant(name))); }
        foreach (var whole in carried.Select(k => shared[k].Script.Main).Append(script.Main))
        {
            // Make the carried top level a function and call it with nothing, for nothing.
            main.Protos.Add(whole);
            code.Add(CompiledScript.Wide(CompiledScript.MakeFunction, 0, main.Protos.Count - 1)); code.Add(CompiledScript.Narrow(CompiledScript.Call, 0, 1, 1));
        }
        code.Add(CompiledScript.Wide(CompiledScript.ReadGlobal, 0, Constant(GoalTable))); code.Add(CompiledScript.Narrow(CompiledScript.NewTable, 1, 0, 0));
        foreach (var part in parts)
        {
            // Takes the table, the character and the goal; calls the source's function with the last two and returns whatever it returns.
            var adapter = new CompiledScript.Function { Parameters = 3, Stack = 6, Constants = [Final(prefixes[0] + "_" + part.Source)] };
            adapter.Code =
            [
                CompiledScript.Wide(CompiledScript.ReadGlobal, 3, 0), CompiledScript.Narrow(CompiledScript.Move, 4, 1, 0), CompiledScript.Narrow(CompiledScript.Move, 5, 2, 0),
                CompiledScript.Narrow(CompiledScript.Call, 3, 3, 0), CompiledScript.Narrow(CompiledScript.Return, 3, 0, 0), CompiledScript.Narrow(CompiledScript.Return, 0, 1, 0),
            ];
            main.Protos.Add(adapter);
            code.Add(CompiledScript.Wide(CompiledScript.MakeFunction, 2, main.Protos.Count - 1));
            code.Add(CompiledScript.Narrow(CompiledScript.SetEntry, 1, CompiledScript.ConstantFrom + Constant(part.Host), 2));
        }
        code.Add(CompiledScript.Narrow(CompiledScript.SetEntry, 0, CompiledScript.ConstantFrom + Constant((double)number), 1));
        code.Add(CompiledScript.Narrow(CompiledScript.Return, 0, 1, 0));
        main.Code = [.. code];
        return new(new CompiledScript(main).Write(), defines, [.. carried.Select(k => shared[k].Name)], [.. unmet]);
    }

    /// <summary>
    /// The list of the globals a binder's script sets, as the host keeps it beside the script: where each name starts, a
    /// closing nought, then the names in two-byte characters, filled out to a multiple of sixteen bytes.
    /// </summary>
    internal static byte[] Names(IReadOnlyList<string> names)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        long at = (names.Count + 1) * 8;
        foreach (string name in names) { writer.Write(at); at += (name.Length + 1) * 2; }
        writer.Write(0L);
        foreach (string name in names) { writer.Write(Encoding.Unicode.GetBytes(name)); writer.Write((short)0); }
        while (stream.Length % 16 != 0) writer.Write((byte)0);
        return stream.ToArray();
    }
}
