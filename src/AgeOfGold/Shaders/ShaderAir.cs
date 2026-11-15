using System.Globalization;
using AgeOfGold.Params;

namespace AgeOfGold.Shaders;

/// <summary>The source's fog and light scattering between a surface and the camera, as instructions of a pixel program.</summary>
internal static class ShaderAir
{
    /// <summary>
    /// Passes linear colours through the air. The source fogs and scatters its gamma-encoded colour, then returns to
    /// linear light.
    /// </summary>
    /// <param name="emit">Adds a named instruction to the program and returns its result.</param>
    /// <param name="view">The vector from the surface to the camera, as the source's points, and fourth the view depth.</param>
    /// <param name="colours">Each colour's three values, with a label that tells its instructions from another colour's.</param>
    internal static string[][] Lit(Func<string, string, string> emit, SourceAir.Values air, string[] view, params (string Label, string[] Colour)[] colours)
    {
        string Unary(string label, int code, string value) => emit(label, $"call float @dx.op.unary.f32(i32 {code}, float {value})");
        string Mad(string label, string a, string b, string c) => emit(label, $"call float @dx.op.tertiary.f32(i32 46, float {a}, float {b}, float {c})");
        string distance = Unary("airdistance", 24, emit("airsquare", $"call float @dx.op.dot3.f32(i32 55, float {view[0]}, float {view[1]}, float {view[2]}, float {view[0]}, float {view[1]}, float {view[2]})"));
        string cosine = emit("aircosine", "fdiv fast float " + emit("airdot", $"call float @dx.op.dot3.f32(i32 55, float {view[0]}, float {view[1]}, float {view[2]}, "
            + $"float {Float(air.SunDirection[0])}, float {Float(air.SunDirection[1])}, float {Float(air.SunDirection[2])})") + $", {distance}");
        string fog = Unary("fog", 7, emit("fogscaled", "fmul fast float " + Unary("fogdepth", 7, emit("fogramp", "fmul fast float "
            + emit("fogoffset", $"fsub fast float {view[3]}, {Float(air.FogBegin)}") + ", " + Float(air.FogScale))) + ", " + Float(air.FogLimit)));
        string rayleigh = Mad("airrayleigh", cosine, cosine, "1.000000e+00");
        string lobe = emit("airlobe", $"fsub fast float {Float(air.Phase[1])}, " + emit("airlobescaled", $"fmul fast float {cosine}, {Float(air.Phase[2])}"));
        // The forward-scattering lobe: its strength over the lobe term to the power one and a half.
        string mie = emit("airmie", "fmul fast float " + emit("airmiebase", "fmul fast float " + emit("airlobeinverse", $"fdiv fast float 1.000000e+00, {lobe}")
            + ", " + Unary("airloberoot", 25, lobe)) + ", " + Float(air.Phase[0]));
        var lit = colours.Select(_ => new string[3]).ToArray();
        for (int i = 0; i < 3; i++)
        {
            char c = "rgb"[i];
            var fogged = colours.Select(colour =>
            {
                string encoded = Unary("airencoded" + colour.Label + c, 21, emit("airgamma" + colour.Label + c, "fmul fast float "
                    + Unary("airlog" + colour.Label + c, 23, Unary("airabs" + colour.Label + c, 6, colour.Colour[i])) + ", " + Float(1 / 2.2f)));
                return Mad("airfogged" + colour.Label + c, emit("airfogdelta" + colour.Label + c, $"fsub fast float {Float(air.FogColour[i])}, {encoded}"), fog, encoded);
            }).ToArray();
            // 2.0813689 is the source's own exponent scale for its base-two extinction.
            string kept = Unary("airkept" + c, 21, emit("airdepth" + c, $"fmul fast float {distance}, {Float(-air.Extinction[i] * air.Distance * 2.0813689f)}"));
            string added = emit("airadded" + c, "fmul fast float " + emit("airgained" + c, "fmul fast float "
                + Mad("airphase" + c, mie, Float(air.BetaDashMie[i]), emit("airphaser" + c, $"fmul fast float {rayleigh}, {Float(air.BetaDashRayleigh[i])}"))
                + ", " + emit("airlost" + c, $"fsub fast float 1.000000e+00, {kept}")) + ", " + Float(air.ReflectanceStrength * air.SunColour[i] / air.Extinction[i]));
            for (int k = 0; k < colours.Length; k++)
            {
                string label = colours[k].Label;
                string scattered = Mad("airscattered" + label + c, emit("airthrough" + label + c, $"fmul fast float {fogged[k]}, {kept}"), Float(air.Reflectance[i]), added);
                string mixed = Mad("airmixed" + label + c, emit("airdelta" + label + c, $"fsub fast float {scattered}, {fogged[k]}"), Float(air.Blend), fogged[k]);
                lit[k][i] = Unary("airlit" + label + c, 21, emit("airlinear" + label + c, "fmul fast float " + Unary("airloglit" + label + c, 23, Unary("airabslit" + label + c, 6, mixed)) + ", " + Float(2.2f)));
            }
        }
        return lit;
    }

    // A float constant in the assembler's exact form: the bits of the equal double.
    private static string Float(float value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
}
