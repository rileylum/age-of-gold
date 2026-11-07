using System.Numerics;
using SoulsFormats;

// Authored binary fixtures only; no installed data is embedded here.
static class FirelinkAmbientCheck
{
    static (byte[] Schema, byte[] Table) Source()
    {
        var schema = new PARAMDEF { ParamType = "LIGHT_BANK", DataVersion = 1 };
        foreach (string suffix in new[] { "u", "d" })
            foreach (string channel in new[] { "R", "G", "B", "A" })
                schema.Fields.Add(new(schema, PARAMDEF.DefType.s16, $"col{channel}_{suffix}"));
        var table = new PARAM { ParamType = schema.ParamType, ParamdefDataVersion = 1, Rows = [] };
        table.ApplyParamdef(schema);
        short[][] values = [[255, 102, 0, 150, 51, 0, 255, 75], [0, 255, 51, 0, 0, 102, 255, 250]];
        foreach (int i in new[] { 0, 1 })
        {
            var row = new PARAM.Row(7 + 2 * i, "Authored", schema);
            for (int j = 0; j < values[i].Length; j++) row.Cells[j].Value = values[i][j];
            table.Rows.Add(row);
        }
        return (schema.Write(), table.Write());
    }

    /// <summary>Check source colour normalization, percentage units, zero strength and values above full strength.</summary>
    public static void Reader()
    {
        var source = Source();
        var read = FirelinkAmbient.Read(source.Schema, source.Table, [7, 9]);
        if (read.Count != 2) throw new InvalidDataException("Expected both authored ambient rows to survive the source reader");
        if (read[7] != new FirelinkAmbient.Ambient(new(1, 0.4f, 0, 1.5f), new(0.2f, 0, 1, 0.75f))
            || read[9] != new FirelinkAmbient.Ambient(new(0, 1, 0.2f, 0), new(0, 0.4f, 1, 2.5f)))
            throw new InvalidDataException("Source RGB normalization or percentage strength changed");
        void rejects(byte[] schema, byte[] table, int[] rows)
        {
            try { FirelinkAmbient.Read(schema, table, rows); }
            catch (InvalidDataException) { return; }
            throw new InvalidDataException("Invalid source ambient input was accepted");
        }
        rejects(source.Schema, source.Table, [123]);
        var invalid = PARAM.Read(source.Table);
        invalid.ApplyParamdef(PARAMDEF.Read(source.Schema));
        invalid.Rows[0]["colR_u"].Value = (short)256;
        rejects(source.Schema, invalid.Write(), [7]);
        invalid.Rows[0]["colR_u"].Value = (short)100;
        invalid.Rows[0]["colA_u"].Value = (short)-1;
        rejects(source.Schema, invalid.Write(), [7]);
        Console.WriteLine("PASS: authored source hemisphere RGB and percentage strengths, including zero and HDR strength");
    }

    /// <summary>Check per-placement selection, time tracks and preservation through written host files.</summary>
    public static void Writer()
    {
        var source = Source();
        var ambient = FirelinkAmbient.Read(source.Schema, source.Table, [7, 9]);
        var graphics = new GPARAM();
        var light = new GPARAM.Group("LightSet ParamEditor", "Authored");
        foreach (string field in new[] { "Directional Light Hemi Color Up", "Directional Light Hemi Color Down", "Directional Light DiffColor0" })
            light.Params.Add(new(field, field, GPARAM.ParamType.Float4) {
                Values = [new Vector4(0.3f, 0.6f, 0.9f, 3), new Vector4(0.9f, 0.6f, 0.3f, 6), Vector4.One],
                ValueIDs = [0, 0, 10], TimeOfDay = [0, 12, 0],
            });
        graphics.Groups.Add(light);
        graphics.Unk3s.Add(new(0) { ValueIDs = [0, 10] });
        var layout = new MSBE();
        layout.Models.MapPieces.Add(new() { Name = "m000001", SibPath = "" });
        foreach (string name in new[] { "authored_a", "authored_b", "authored_c", "untouched" })
            layout.Parts.MapPieces.Add(new() { Name = name, ModelName = "m000001", SibPath = "", Gparam = new() { LightSetID = -1 } });
        var assignments = new Dictionary<string, int> { ["authored_a"] = 7, ["authored_b"] = 9, ["authored_c"] = 7 };
        var graphicsInput = graphics.Write(); var layoutInput = layout.Write();
        var written = FirelinkAmbient.Write(graphicsInput, layoutInput, assignments, ambient);
        var actual = MSBE.Read(written.Layout);
        if (actual.Parts.MapPieces.Single(p => p.Name == "authored_a").Gparam.LightSetID != 1
            || actual.Parts.MapPieces.Single(p => p.Name == "authored_b").Gparam.LightSetID != 2
            || actual.Parts.MapPieces.Single(p => p.Name == "authored_c").Gparam.LightSetID != 1)
            throw new InvalidDataException("Written scenery must select its source ambient row using distinct unused host light sets");
        var read = GPARAM.Read(written.Graphics);
        foreach (var (sourceRow, hostId) in written.LightSets)
            foreach (var param in read.Groups.Single().Params)
            {
                var entries = Enumerable.Range(0, param.Values.Count).Where(i => param.ValueIDs[i] == hostId).ToArray();
                if (!entries.Select(i => param.TimeOfDay[i]).SequenceEqual(new[] {0f, 12f}))
                    throw new InvalidDataException("Ambient light set changed the host time track");
                for (int j = 0; j < entries.Length; j++)
                {
                    var expected = param.Name1.EndsWith(" Up") ? ambient[sourceRow].Upper
                        : param.Name1.EndsWith(" Down") ? ambient[sourceRow].Lower
                        : (Vector4)light.Params.Single(p => p.Name1 == param.Name1).Values[j];
                    if ((Vector4)param.Values[entries[j]] != expected)
                        throw new InvalidDataException("Written ambient or inherited direct light differs from the authored expectation");
                }
            }
        if (!read.Unk3s.Single().ValueIDs.SequenceEqual(new[] {0, 1, 2, 10}))
            throw new InvalidDataException("Light group index does not include the new light sets");
        foreach (var param in read.Groups.Single().Params)
            for (int i = param.Values.Count - 1; i >= 0; i--)
                if (param.ValueIDs[i] is 1 or 2) { param.Values.RemoveAt(i); param.ValueIDs.RemoveAt(i); param.TimeOfDay.RemoveAt(i); }
        read.Unk3s.Single().ValueIDs.RemoveAll(id => id is 1 or 2);
        foreach (var part in actual.Parts.MapPieces.Where(p => assignments.ContainsKey(p.Name))) part.Gparam.LightSetID = -1;
        if (!read.Write().AsSpan().SequenceEqual(graphicsInput) || !actual.Write().AsSpan().SequenceEqual(layoutInput))
            throw new InvalidDataException("Ambient translation changed an unrelated graphics or map setting");
        var repeat = FirelinkAmbient.Write(graphicsInput, layoutInput, assignments, ambient);
        if (!repeat.Graphics.AsSpan().SequenceEqual(written.Graphics) || !repeat.Layout.AsSpan().SequenceEqual(written.Layout))
            throw new InvalidDataException("Ambient output does not repeat byte for byte");
        Console.WriteLine("PASS: authored per-placement ambient sets, preserved sun/time tracks and unrelated bytes, deterministic output");
    }
}
