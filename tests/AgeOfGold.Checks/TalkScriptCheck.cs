using AgeOfGold.Talk;
using SoulsFormats;

internal static class TalkScriptCheck
{
    internal static void Run()
    {
        var bytes = TalkScript.OneMessage(9100000, 9100001);
        if (!bytes.SequenceEqual(TalkScript.OneMessage(9100000, 9100001))) throw new Exception("Talk script does not repeat");
        var script = ESD.Read(bytes);
        if (!script.LongFormat || script.DarkSoulsCount != 3 || script.StateGroups.Count != 1 || script.StateGroups[1].Count != 6) throw new Exception("Talk script layout differs");
        var states = script.StateGroups[1];
        // Expected bytes are written out from the script encoding: a small whole number is itself plus 0x40; 0x82 leads a
        // four-byte whole number, 0x80 a four-byte real and 0x81 an eight-byte real; a call pushes the function's number and
        // its arguments, then 0x84 plus the argument count; 0x94 is "greater", 0x95 "equal"; 0xA1 ends every expression.
        void Leads(int state, int target, string test)
        {
            var condition = states[state].Conditions.Single();
            if (condition.TargetState != target || Convert.ToHexString(condition.Evaluator) != test || condition.PassCommands.Count != 0 || condition.Subconditions.Count != 0)
                throw new Exception($"Talk state {state} leaves differently: {condition.TargetState} when {Convert.ToHexString(condition.Evaluator)}");
        }
        void Enters(int state, params string[] commands)
        {
            var actual = states[state].EntryCommands.Select(c => $"{c.CommandBank}:{c.CommandID} " + string.Join("|", c.Arguments.Select(Convert.ToHexString))).ToArray();
            if (!actual.SequenceEqual(commands) || states[state].ExitCommands.Count != 0 || states[state].WhileCommands.Count != 0)
                throw new Exception($"Talk state {state} enters differently: " + string.Join("; ", actual));
        }
        Leads(0, 1, "41A1"); Enters(0);
        // Start: stay awake within thirty metres.
        Enters(1, "1:38 5EA1"); Leads(1, 2, "41A1");
        // Wait for the action (host function 56 with the action's number).
        Enters(2); Leads(2, 3, "7882E0DA8A0085A1");
        // Clear the action and open the message as a one-button dialog; move on once it has had time to open.
        Enters(3, "1:35 ", "1:17 47A1|82E1DA8A00A1|41A1|40A1|41A1"); Leads(3, 4, "826700000084819A9999999999C93F94A1");
        // Wait for the dialog to close (function 58 of player zero is zero), clear again, and return to waiting.
        Enters(4); Leads(4, 5, "7A40854095A1");
        Enters(5, "1:35 "); Leads(5, 2, "826700000084800000003F94A1");
        // Small numbers take the one-byte form.
        var small = ESD.Read(TalkScript.OneMessage(5, 63)).StateGroups[1];
        if (Convert.ToHexString(small[2].Conditions.Single().Evaluator) != "784585A1" || Convert.ToHexString(small[3].EntryCommands[1].Arguments[1]) != "7FA1")
            throw new Exception("Small talk numbers are not in their one-byte form");
        foreach (var (action, message) in new[] { (0, 1), (1, 0), (-5, 9) })
        {
            try { TalkScript.OneMessage(action, message); }
            catch (InvalidDataException) { continue; }
            throw new Exception("An invalid talk identity was accepted");
        }
        Console.WriteLine("PASS: a one-message talk script waits for its action, opens its message and repeats, in the host's encoding");
    }
}
