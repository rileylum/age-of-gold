using System.Buffers.Binary;
using System.Numerics;
using AgeOfGold.Params;
using SoulsFormats;

internal static class GameplayTablesCheck
{
    internal static void Run()
    {
        var source = Fixture("ACTIONBUTTON_PARAM_ST", 2, 100);
        var before = source.Write();
        var output = GameplayTables.AddInteraction(before, 41, 73, "authored interaction", 187);
        var actual = Read(output, 100);
        var expected = Payload(source.Rows[0]).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(52), 187);
        var added = actual.Rows.Single(r => r.ID == 73);
        Require(added.Name == "authored interaction" && Payload(added).SequenceEqual(expected),
            "interaction must retain its template except the prompt reference");
        actual.Rows.Remove(added);
        Require(actual.Write().SequenceEqual(before), "interaction changed an existing row or table metadata");
        Require(GameplayTables.AddInteraction(before, 41, 73, "authored interaction", 187).SequenceEqual(output),
            "interaction edit is not deterministic");
        Console.WriteLine("PASS: authored interaction edit preserves template bytes, other rows and table metadata");

        var resting = Fixture("BONFIRE_WARP_PARAM_ST", 6, 236);
        BinaryPrimitives.WriteUInt32LittleEndian(Payload(resting.Rows[0]).AsSpan(8), 137);
        var destination = new GameplayTables.Destination(91, "authored resting point", 271, 539,
            24, 3, 8, new Vector3(11.25f, -2.5f, 37.75f), 67);
        var originalResting = resting.Write();
        var replaced = GameplayTables.ReplaceDestinations(originalResting, 137, destination);
        var actualResting = Read(replaced, 236);
        Require(actualResting.Rows.Count == 1, "host destinations remain reachable");
        var placed = actualResting.Rows.Single();
        Require(placed.ID == 91 && placed.Name == "authored resting point", "resting identity differs");
        var desired = Payload(resting.Rows[0]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(desired.AsSpan(4), 539);
        BinaryPrimitives.WriteUInt32LittleEndian(desired.AsSpan(8), 271);
        BinaryPrimitives.WriteInt32LittleEndian(desired.AsSpan(20), -1);
        new byte[] { 24, 3, 8 }.CopyTo(desired, 32);
        BinaryPrimitives.WriteSingleLittleEndian(desired.AsSpan(36), 11.25f);
        BinaryPrimitives.WriteSingleLittleEndian(desired.AsSpan(40), -2.5f);
        BinaryPrimitives.WriteSingleLittleEndian(desired.AsSpan(44), 37.75f);
        BinaryPrimitives.WriteInt32LittleEndian(desired.AsSpan(48), 67);
        Require(Payload(placed).SequenceEqual(desired), "resting placement or retained template bytes differ");
        actualResting.Rows = resting.Rows;
        Require(actualResting.Write().SequenceEqual(originalResting), "resting table metadata changed");
        Require(GameplayTables.ReplaceDestinations(originalResting, 137, destination).SequenceEqual(replaced),
            "resting edit is not deterministic");
        Console.WriteLine("PASS: authored resting edit relocates one destination, removes host destinations and preserves other fields");
        Reject(() => GameplayTables.ReplaceDestinations(originalResting, 137,
            destination with { Position = new Vector3(float.NaN, 1, 2) }), "non-finite resting placement");
        Reject(() => GameplayTables.ReplaceDestinations(originalResting, 137,
            destination with { UnlockFlag = BinaryPrimitives.ReadUInt32LittleEndian(Payload(resting.Rows[1]).AsSpan(4)) }),
            "occupied resting flag");
        Reject(() => GameplayTables.ReplaceDestinations(originalResting, 137, destination with { Entity = 137 }), "occupied resting entity");
        Reject(() => GameplayTables.ReplaceDestinations(originalResting, 999, destination), "missing resting template");
        BinaryPrimitives.WriteUInt32LittleEndian(Payload(resting.Rows[1]).AsSpan(8), 137);
        Reject(() => GameplayTables.ReplaceDestinations(resting.Write(), 137, destination), "ambiguous resting template");
        Reject(() => GameplayTables.AddInteraction(before, 999, 73, "authored", 187), "missing interaction template");
        Reject(() => GameplayTables.AddInteraction(before, 41, 42, "authored", 187), "occupied row identity");
        Reject(() => GameplayTables.AddInteraction(before, 41, 73, "authored", -1), "missing prompt reference");
        source.ParamdefDataVersion++;
        Reject(() => GameplayTables.AddInteraction(source.Write(), 41, 73, "authored", 187), "unknown row revision");
        // A host ambient effect is silenced by clearing its effect reference; the row stays for whatever applies it.
        var effects = Fixture("SP_EFFECT_VFX_PARAM_ST", 1, 164);
        var effectBytes = effects.Write();
        int ambient = effects.Rows[1].ID;
        var silenced = Read(GameplayTables.SilenceEffect(effectBytes, ambient), 164);
        Require(BitConverter.ToInt32(Payload(silenced.Rows[1]), 0) == -1 && Payload(silenced.Rows[1]).AsSpan(4).SequenceEqual(Payload(effects.Rows[1]).AsSpan(4))
            && Payload(silenced.Rows[0]).SequenceEqual(Payload(effects.Rows[0])) && silenced.Rows.Count == effects.Rows.Count, "ambient effect row was not silenced exactly");
        Require(GameplayTables.SilenceEffect(effectBytes, ambient).SequenceEqual(GameplayTables.SilenceEffect(effectBytes, ambient)), "effect table rewrite differs across runs");
        Reject(() => GameplayTables.SilenceEffect(effectBytes, 987654), "missing ambient effect row");
        Reject(() => GameplayTables.SilenceEffect(GameplayTables.SilenceEffect(effectBytes, ambient), ambient), "an effect row with no effect to silence");
        Reject(() => GameplayTables.SilenceEffect(Fixture("SP_EFFECT_VFX_PARAM_ST", 1, 168).Write(), ambient), "unsupported effect row size");
        // A build-owned character row: an installed row with another body size, under its own identity.
        var characters = Fixture("NPC_PARAM_ST", 9, 736);
        var characterBytes = characters.Write();
        var sized = Read(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", (1.5f, 0.35f)), 736);
        var body = Payload(characters.Rows[1]).ToArray();
        // Both of the row's body-size pairs take the new size: the one the map sees and the one other characters see.
        foreach (int at in new[] { 20, 340 }) { BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(at), 1.5f); BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(at + 4), 0.35f); }
        Require(sized.Rows.Count == 3 && sized.Rows.Single(r => r.ID == 73).Name == "authored character" && Payload(sized.Rows.Single(r => r.ID == 73)).SequenceEqual(body)
            && Payload(sized.Rows.Single(r => r.ID == 42)).SequenceEqual(Payload(characters.Rows[1])), "character row is not its template with the new body size");
        Require(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", (1.5f, 0.35f)).SequenceEqual(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", (1.5f, 0.35f))), "character row edit is not deterministic");
        // A new row goes where its identity belongs among the table's ascending rows.
        Require(Read(GameplayTables.AddCharacter(characterBytes, 42, 7, "authored character", null), 736).Rows.Select(r => r.ID).SequenceEqual([7, 41, 42])
            && sized.Rows.Select(r => r.ID).SequenceEqual([41, 42, 73]), "a new row is not placed in identity order");
        // With a display mark the row draws that mark's meshes alone, of its first sixteen.
        var marked = Read(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", (1.5f, 0.35f), 10), 736);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(330), 1 << 10);
        Require(Payload(marked.Rows.Single(r => r.ID == 73)).SequenceEqual(body), "character row does not show its one display mark");
        // Without a body size the row keeps its template's and takes only the mark.
        var kept = Payload(characters.Rows[1]).ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(kept.AsSpan(330), 1 << 14);
        Require(Payload(Read(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", null, 14), 736).Rows.Single(r => r.ID == 73)).SequenceEqual(kept), "character row without a size changed its template's body");
        // With another row's standing the new row takes that row's kind and side, and nothing else of it.
        kept[304] = Payload(characters.Rows[0])[304]; kept[307] = Payload(characters.Rows[0])[307];
        Require(Payload(Read(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", null, 14, 41), 736).Rows.Single(r => r.ID == 73)).SequenceEqual(kept), "character row does not take another row's standing alone");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", null, 14, 999), "a missing standing row");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", (1.5f, 0.35f), 16), "a display mark outside the row's field");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 999, 73, "authored", (1.5f, 0.35f)), "missing character template");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 41, "authored", (1.5f, 0.35f)), "occupied character row");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", (0, 0.35f)), "a body without height");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", (1.5f, float.NaN)), "a body without width");
        Reject(() => GameplayTables.AddCharacter(Fixture("NPC_PARAM_ST", 9, 740).Write(), 42, 73, "authored", (1.5f, 0.35f)), "unsupported character row size");
        // A character row may name the behaviour variation whose rows answer its attacks: four bytes in, after the host's own.
        var varied = Payload(Read(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", null, null, null, 25400), 736).Rows.Single(r => r.ID == 73));
        var expectedVaried = Payload(characters.Rows[1]).ToArray(); BinaryPrimitives.WriteInt32LittleEndian(expectedVaried.AsSpan(4), 25400);
        Require(varied.SequenceEqual(expectedVaried), "character row does not name its behaviour variation, or changed elsewhere");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", null, null, null, 0), "a behaviour variation of nothing");
        Console.WriteLine("PASS: a character row names the behaviour variation it is given and is otherwise its template");

        // A build-owned think row: an installed row with what a source think row says of the same things. The two games keep
        // them at different places: the battle number four bytes into the source's row and eight into the host's, and so on.
        var thinking = Fixture("NPC_THINK_PARAM_ST", 2, 228);
        var thinkingBytes = thinking.Write();
        var sourceThink = new byte[112];
        BinaryPrimitives.WriteInt32LittleEndian(sourceThink.AsSpan(4), 254000); BinaryPrimitives.WriteSingleLittleEndian(sourceThink.AsSpan(24), 4.5f);
        // How far it sees, then (after hearing, which the host keeps otherwise) nine more whole numbers of distances and times.
        foreach (var (at, value) in new[] { (48, 30), (52, 3), (54, 4), (56, 75), (58, 50), (60, 6), (62, 5), (64, 20), (66, 21), (68, 600), (70, 300), (72, 19) }) BinaryPrimitives.WriteUInt16LittleEndian(sourceThink.AsSpan(at), (ushort)value);
        sourceThink[50] = 99; sourceThink[80] = 25; sourceThink[81] = 120; sourceThink[82] = 110; sourceThink[83] = 77;
        // What it does out of battle is a number of the host's, given apart: four bytes into the host's row.
        var thought = Read(GameplayTables.AddThinking(thinkingBytes, 42, 73, "authored thinking", sourceThink, 10000), 228);
        var expectedThought = Payload(thinking.Rows[1]).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expectedThought.AsSpan(4), 10000); BinaryPrimitives.WriteInt32LittleEndian(expectedThought.AsSpan(8), 254000); BinaryPrimitives.WriteSingleLittleEndian(expectedThought.AsSpan(28), 4.5f);
        foreach (var (at, value) in new[] { (52, 30), (56, 3), (58, 4), (60, 75), (62, 50), (64, 6), (66, 5), (68, 20), (70, 21), (76, 19) }) BinaryPrimitives.WriteUInt16LittleEndian(expectedThought.AsSpan(at), (ushort)value);
        // The two times after which it forgets what it saw and heard are whole seconds in the source and fractions in the host.
        BinaryPrimitives.WriteSingleLittleEndian(expectedThought.AsSpan(32), 600f); BinaryPrimitives.WriteSingleLittleEndian(expectedThought.AsSpan(72), 300f);
        expectedThought[84] = 25; expectedThought[85] = 120; expectedThought[86] = 110;
        Require(thought.Rows.Select(r => r.ID).SequenceEqual([41, 42, 73]) && thought.Rows[2].Name == "authored thinking" && Payload(thought.Rows[2]).SequenceEqual(expectedThought)
            && Payload(thought.Rows[1]).SequenceEqual(Payload(thinking.Rows[1])), "think row is not its template with the source row's battle number, senses and distances");
        Require(GameplayTables.BattleOf(thought.Write(), 73) == 254000 && GameplayTables.BattleOf(thinkingBytes, 42) == BinaryPrimitives.ReadInt32LittleEndian(Payload(thinking.Rows[1]).AsSpan(8)), "a think row's battle number is read otherwise");
        Reject(() => GameplayTables.BattleOf(thinkingBytes, 999), "a missing think row");
        Reject(() => GameplayTables.AddThinking(thinkingBytes, 999, 73, "authored", sourceThink, 10000), "missing think template");
        Reject(() => GameplayTables.AddThinking(thinkingBytes, 42, 41, "authored", sourceThink, 10000), "occupied think row");
        Reject(() => GameplayTables.AddThinking(thinkingBytes, 42, 73, "authored", new byte[112], 10000), "a battle number of nothing");
        Reject(() => GameplayTables.AddThinking(thinkingBytes, 42, 73, "authored", new byte[116], 10000), "a source think row of another length");
        Reject(() => GameplayTables.AddThinking(thinkingBytes, 42, 73, "authored", sourceThink, 0), "out-of-battle thinking of no number");
        Reject(() => GameplayTables.AddThinking(Fixture("NPC_THINK_PARAM_ST", 2, 232).Write(), 42, 73, "authored", sourceThink, 10000), "unsupported think row size");
        Console.WriteLine("PASS: a think row takes a source row's battle number, senses, distances and times, each where the host keeps it, and the out-of-battle number it is given, and is otherwise its template");

        // A source character's behaviour rows go into the host's table as they are: the two games lay them out alike.
        var behaviours = Fixture("BEHAVIOR_PARAM_ST", 2, 32);
        var behaviourBytes = behaviours.Write();
        byte[] Behaviour(int variation, int asked, int refers) { var row = new byte[32]; BinaryPrimitives.WriteInt32LittleEndian(row, variation); BinaryPrimitives.WriteInt32LittleEndian(row.AsSpan(4), asked); row[8] = 2; BinaryPrimitives.WriteInt32LittleEndian(row.AsSpan(12), refers); return row; }
        var asked = new[] { (225400110, Behaviour(25400, 110, 254001)), (30, Behaviour(25400, 100, 254000)) };
        var answered = Read(GameplayTables.AddBehaviours(behaviourBytes, asked), 32);
        Require(answered.Rows.Select(r => r.ID).SequenceEqual([30, 41, 42, 225400110]) && Payload(answered.Rows[0]).SequenceEqual(asked[1].Item2) && Payload(answered.Rows[3]).SequenceEqual(asked[0].Item2)
            && Payload(answered.Rows[1]).SequenceEqual(Payload(behaviours.Rows[0])) && Payload(answered.Rows[2]).SequenceEqual(Payload(behaviours.Rows[1])), "behaviour rows were not added as given, in ascending order");
        Reject(() => GameplayTables.AddBehaviours(behaviourBytes, [(41, Behaviour(25400, 100, 254000))]), "an occupied behaviour row");
        Reject(() => GameplayTables.AddBehaviours(behaviourBytes, [(30, new byte[31])]), "a behaviour row of another length");
        // A variation the host already answers would mix two characters' attacks.
        var taken = Fixture("BEHAVIOR_PARAM_ST", 2, 32); BinaryPrimitives.WriteInt32LittleEndian(Payload(taken.Rows[0]), 25400);
        Reject(() => GameplayTables.AddBehaviours(taken.Write(), [(30, Behaviour(25400, 100, 254000))]), "a behaviour variation the host already uses");
        Reject(() => GameplayTables.AddBehaviours(Fixture("BEHAVIOR_PARAM_ST", 2, 36).Write(), asked), "unsupported behaviour row size");
        Console.WriteLine("PASS: source behaviour rows join the host table unchanged; occupied rows and variations fail");

        // An attack row is a host row of a like attack with the source's hit sizes, attachment points and damage laid over it.
        var attacks = Fixture("ATK_PARAM_ST", 4, 456);
        var attackBytes = attacks.Write();
        var sourceAttack = Enumerable.Range(0, 128).Select(i => (byte)(200 - i)).ToArray();
        var hit = Payload(Read(GameplayTables.AddAttacks(attackBytes, 42, [(254000, sourceAttack)]), 456).Rows.Single(r => r.ID == 254000));
        var expectedHit = Payload(attacks.Rows[1]).ToArray();
        foreach (var (from, length) in new[] { (0, 24), (44, 16), (80, 12) }) sourceAttack.AsSpan(from, length).CopyTo(expectedHit.AsSpan(from));
        Require(hit.SequenceEqual(expectedHit), "attack row does not take the source's hit sizes, attachment points and damage over its template");
        Reject(() => GameplayTables.AddAttacks(attackBytes, 999, [(254000, sourceAttack)]), "a missing attack template");
        Reject(() => GameplayTables.AddAttacks(attackBytes, 42, [(41, sourceAttack)]), "an occupied attack row");
        Reject(() => GameplayTables.AddAttacks(attackBytes, 42, [(254000, new byte[124])]), "a source attack row of another length");
        Reject(() => GameplayTables.AddAttacks(Fixture("ATK_PARAM_ST", 4, 460).Write(), 42, [(254000, sourceAttack)]), "unsupported attack row size");
        Console.WriteLine("PASS: an attack row is its host template under the source's hit sizes, attachment points and damage");

        var wider = Fixture("ACTIONBUTTON_PARAM_ST", 2, 104);
        Reject(() => GameplayTables.AddInteraction(wider.Write(), 41, 73, "authored", 187), "unexpected row length");
        wider = Fixture("ACTIONBUTTON_PARAM_ST", 2, 100);
        wider.Rows[1].ID = wider.Rows[0].ID;
        Reject(() => GameplayTables.AddInteraction(wider.Write(), 41, 73, "authored", 187), "duplicate input identities");
        Require(source.Rows[0].ID == 41 && before.SequenceEqual(Read(before, 100).Write()), "failed edit changed its input");
        Console.WriteLine("PASS: unsupported table layouts, ambiguous templates, occupied identities and invalid placements fail");
    }

    internal static PARAM Fixture(string type, short version, int size)
    {
        var def = Definition(type, version, size);
        var table = new PARAM
        {
            ParamType = type, ParamdefDataVersion = version, Rows = [],
            Format2D = PARAM.FormatFlags1.Flag01 | PARAM.FormatFlags1.LongDataOffset | PARAM.FormatFlags1.OffsetParamType,
            Format2E = PARAM.FormatFlags2.UnicodeRowNames | PARAM.FormatFlags2.Flag02 | PARAM.FormatFlags2.Flag04,
        };
        table.ApplyParamdef(def);
        foreach (int id in new[] { 41, 42 })
        {
            var row = new PARAM.Row(id, "authored " + id, def);
            row["payload"].Value = Enumerable.Range(0, size).Select(i => (byte)((i * 13 + id) % 256)).ToArray();
            table.Rows.Add(row);
        }
        return table;
    }

    internal static PARAM Read(byte[] bytes, int size)
    {
        var table = PARAM.Read(bytes);
        table.ApplyParamdef(Definition(table.ParamType, table.ParamdefDataVersion, size));
        return table;
    }

    internal static byte[] Payload(PARAM.Row row) => (byte[])row["payload"].Value;

    private static PARAMDEF Definition(string type, short version, int size)
    {
        var def = new PARAMDEF { ParamType = type, DataVersion = version };
        def.Fields.Add(new PARAMDEF.Field(def, PARAMDEF.DefType.dummy8, "payload") { ArrayLength = size });
        return def;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action, string description)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Accepted " + description);
    }
}
