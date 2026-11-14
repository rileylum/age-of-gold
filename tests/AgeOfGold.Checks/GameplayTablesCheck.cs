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
        var sized = Read(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", 1.5f, 0.35f), 736);
        var body = Payload(characters.Rows[1]).ToArray();
        // Both of the row's body-size pairs take the new size: the one the map sees and the one other characters see.
        foreach (int at in new[] { 20, 340 }) { BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(at), 1.5f); BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(at + 4), 0.35f); }
        Require(sized.Rows.Count == 3 && sized.Rows.Single(r => r.ID == 73).Name == "authored character" && Payload(sized.Rows.Single(r => r.ID == 73)).SequenceEqual(body)
            && Payload(sized.Rows.Single(r => r.ID == 42)).SequenceEqual(Payload(characters.Rows[1])), "character row is not its template with the new body size");
        Require(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", 1.5f, 0.35f).SequenceEqual(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", 1.5f, 0.35f)), "character row edit is not deterministic");
        // With a display mark the row draws that mark's meshes alone, of its first sixteen.
        var marked = Read(GameplayTables.AddCharacter(characterBytes, 42, 73, "authored character", 1.5f, 0.35f, 10), 736);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(330), 1 << 10);
        Require(Payload(marked.Rows.Single(r => r.ID == 73)).SequenceEqual(body), "character row does not show its one display mark");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", 1.5f, 0.35f, 16), "a display mark outside the row's field");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 999, 73, "authored", 1.5f, 0.35f), "missing character template");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 41, "authored", 1.5f, 0.35f), "occupied character row");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", 0, 0.35f), "a body without height");
        Reject(() => GameplayTables.AddCharacter(characterBytes, 42, 73, "authored", 1.5f, float.NaN), "a body without width");
        Reject(() => GameplayTables.AddCharacter(Fixture("NPC_PARAM_ST", 9, 740).Write(), 42, 73, "authored", 1.5f, 0.35f), "unsupported character row size");
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
