using System.Buffers.Binary;
using AgeOfGold;
using AgeOfGold.Shaders;

internal static class SourceShaderParametersCheck
{
    internal static void Fixtures(string input, string output)
    {
        if (Directory.Exists(output) || !Install.Within(Install.Canonical(output), Install.Canonical(Path.GetTempPath())))
            throw new ArgumentException("Use fresh parameter output under system temp");
        foreach (string name in new[] { "constants", "packed", "constants-sm4", "matrices", "selector", "matrices-column", "values", "values-off" })
        {
            var values = new Dictionary<(string, string), Array>();
            if (name == "selector") values.Add(("Settings", "mode"), new uint[] { 3 });
            else if (name.StartsWith("matrices", StringComparison.Ordinal))
                values.Add(("Frames", "frames"), new float[] { 2.5f, 0, 0, 0, 0, .625f, -1.25f, 3, 0, 0, 0, -.375f });
            else if (name.StartsWith("values", StringComparison.Ordinal))
            {
                values.Add(("Settings", "enabled"), new[] { name == "values" });
                values.Add(("Settings", "signedValue"), new[] { -7 });
                values.Add(("Settings", "token"), new uint[] { 0xa0000000 });
                values.Add(("Settings", "vectors"), new float[] { 2, 3, 4, 6, 7, 8 });
            }
            else
            {
                values.Add(("$Globals", "amount"), new[] { 2.5f });
                values.Add(("$Globals", "axis"), new[] { -1.25f, 3f });
                values.Add(("Settings", "control"), new[] { .75f });
                values.Add(("Settings", "weights"), name == "packed" ? new[] { .125f, .5f } : new[] { 0f, 0f, .125f, .5f });
            }
            var program = File.ReadAllBytes(Path.Combine(input, (name == "values-off" ? "values" : name) + ".dxbc"));
            var bound = SourceShaderParameters.Bind(program, values);
            if (name == "matrices" && !bound[0].SequenceEqual(
                SourceShaderParameters.Bind(SourceShaderConstantsCheck.WithUnusedPrefix(program), values)[0]))
                throw new Exception("Unused source alias changed bound parameters");
            var directory = Path.Combine(output, name);
            Directory.CreateDirectory(directory);
            foreach (uint slot in new uint[] { 0, 2 })
                File.WriteAllBytes(Path.Combine(directory, "b" + slot + ".bin"), bound.GetValueOrDefault(slot, new byte[16]));
        }
        Console.WriteLine("PASS: source parameter fixtures bind scalars, packed vectors, matrix arrays and integer selectors");
    }

    internal static void Run()
    {
        var (program, locations) = SourceShaderConstantsCheck.Fixture();
        var values = new Dictionary<(string, string), Array> {
            [("Authored", "first")] = new[] { 1.5f }, [("Authored", "last")] = new[] { -2.25f } };
        var bound = SourceShaderParameters.Bind(program, values);
        if (bound.Count != 1 || !bound.TryGetValue(3, out var bytes) || bytes.Length != 64)
            throw new Exception("Named source parameters were not bound to their reflected buffer");
        var expected = new byte[64];
        BinaryPrimitives.WriteSingleLittleEndian(expected, 1.5f);
        BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(48), -2.25f);
        if (!bytes.SequenceEqual(expected)) throw new Exception("Named source values lost their reflected positions or padding");
        var reversed = values.Reverse().ToDictionary(p => p.Key, p => p.Value);
        if (!SourceShaderParameters.Bind(program, reversed)[3].SequenceEqual(bytes))
            throw new Exception("Source parameter bytes depend on dictionary order");
        Reject(new Dictionary<(string, string), Array>(), "every active");
        foreach (Array bad in new Array[] { new[] { 1 }, new[] { 1f, 2f }, new[] { float.NaN }, new[] { float.PositiveInfinity } })
            Reject(new Dictionary<(string, string), Array>(values) { [("Authored", "first")] = bad }, "type or shape");
        Reject(new Dictionary<(string, string), Array>(values) { [("Unknown", "first")] = new[] { 0f } }, "every active");
        foreach (var (scalar, first, last, expectedFirst, expectedLast) in new (uint, Array, Array, uint, uint)[] {
            (1, new[] { true }, new[] { false }, 1, 0),
            (2, new[] { int.MinValue }, new[] { -3 }, 0x80000000, 0xfffffffd),
            (19, new[] { uint.MaxValue }, new uint[] { 5 }, uint.MaxValue, 5),
            (3, new[] { -0f }, new[] { 0f }, 0x80000000, 0) })
        {
            var typed = program.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(typed.AsSpan(locations["typeClass"]), scalar << 16);
            var data = SourceShaderParameters.Bind(typed, new Dictionary<(string, string), Array> {
                [("Authored", "first")] = first, [("Authored", "last")] = last })[3];
            if (BinaryPrimitives.ReadUInt32LittleEndian(data) != expectedFirst
                || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48)) != expectedLast)
                throw new Exception("Source scalar encoding changed bits");
        }
        Console.WriteLine("PASS: named source parameters bind at reflected positions with deterministic padding");

        void Reject(IReadOnlyDictionary<(string, string), Array> bad, string diagnostic)
        {
            try { SourceShaderParameters.Bind(program, bad); }
            catch (InvalidDataException e) when (e.Message.Contains(diagnostic)) { return; }
            throw new Exception("Invalid named source parameters were accepted or misdiagnosed");
        }
    }
}
