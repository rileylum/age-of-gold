using System.Numerics;
using SoulsFormats;

// Spike only: isolate the host's main-light direction from brightness and material changes.
static class FirelinkLighting
{
    /// <summary>Turn the main directional-light track halfway around without changing its elevation or timing.</summary>
    public static byte[] ReverseDirection(byte[] input)
    {
        var graphics = GPARAM.Read(input);
        if (graphics.Game != GPARAM.GPGame.Sekiro)
            throw new InvalidDataException("Unclassified graphics-parameter version");
        var angles = graphics.Groups.Single(g => g.Name1 == "LightSet ParamEditor")
            .Params.Single(p => p.Name1 == "Directional Light Angle0");
        if (angles.Type != GPARAM.ParamType.Float2 || angles.Values.Count == 0)
            throw new InvalidDataException("Expected a nonempty main-light angle track");
        for (int i = 0; i < angles.Values.Count; i++)
        {
            var angle = (Vector2)angles.Values[i];
            if (!float.IsFinite(angle.X) || !float.IsFinite(angle.Y))
                throw new InvalidDataException("Main-light angle must be finite");
            // Keep the entire track unwrapped so interpolation still follows the same path.
            angles.Values[i] = new Vector2(angle.X, angle.Y + MathF.PI);
        }
        return graphics.Write();
    }

    /// <summary>Check the written direction track and preservation of unrelated settings with an authored graphics file.</summary>
    public static void Check()
    {
        var authored = new GPARAM();
        var light = new GPARAM.Group("LightSet ParamEditor", "Authored light");
        var angles = new GPARAM.Param("Directional Light Angle0", "Angle", GPARAM.ParamType.Float2) {
            Values = [new Vector2(-0.25f, 0), new Vector2(-0.75f, MathF.PI * 0.75f)],
            ValueIDs = [7, 7], TimeOfDay = [0, 12],
        };
        light.Params.Add(angles);
        light.Params.Add(new GPARAM.Param("Directional Light DiffColor0", "Colour", GPARAM.ParamType.Float4) {
            Values = [new Vector4(0.25f, 0.5f, 0.75f, 2)], ValueIDs = [7], TimeOfDay = [0],
        });
        authored.Groups.Add(light);
        var input = authored.Write();
        var written = GPARAM.Read(ReverseDirection(input));
        var actual = written.Groups.Single().Params.First();
        Vector2[] expected = [new(-0.25f, MathF.PI), new(-0.75f, MathF.PI * 1.75f)];
        for (int i = 0; i < expected.Length; i++)
            if (Vector2.Distance((Vector2)actual.Values[i], expected[i]) > 0.000001f)
                throw new InvalidDataException("Written light must reverse horizontal direction while preserving elevation and an unwrapped track");
        actual.Values = angles.Values;
        if (!written.Write().AsSpan().SequenceEqual(input))
            throw new InvalidDataException("Light-direction diagnostic changed another setting or keyframe");
        Console.WriteLine("PASS: authored light reverses direction; elevation, intensity, colour and timing are unchanged");
    }
}
