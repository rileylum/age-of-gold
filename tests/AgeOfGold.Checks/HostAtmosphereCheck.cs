using AgeOfGold.Maps;
using SoulsFormats;

internal static class HostAtmosphereCheck
{
    internal static void Run()
    {
        var file = new GPARAM { Game = GPARAM.GPGame.Sekiro, Groups = [], Unk3s = [], UnkBlock2 = [] };
        GPARAM.Param Add(GPARAM.Group group, string name, GPARAM.ParamType type, params object[] values)
        {
            var param = new GPARAM.Param(name, "authored", type) { Values = values.ToList(), ValueIDs = values.Select((_, i) => i).ToList(), TimeOfDay = values.Select((_, i) => (float)i).ToList() };
            group.Params.Add(param);
            return param;
        }
        var tone = new GPARAM.Group("Authored Tone", "tone") { Params = [], Comments = [] };
        var air = new GPARAM.Group("Authored Air", "air") { Params = [], Comments = [] };
        file.Groups.Add(tone); file.Groups.Add(air);
        Add(tone, "GS Presents - Chromatic Aberration Enable", GPARAM.ParamType.IntB, 1);
        Add(tone, "GS Presents - Vignetting Power", GPARAM.ParamType.Float, 0.42f, 0.53f);
        Add(tone, "Authored Exposure", GPARAM.ParamType.Float, 1.25f);
        Add(air, "VolumetricFog Enable", GPARAM.ParamType.BoolB, true);
        Add(air, "VolumetricFog Authored Density", GPARAM.ParamType.Float, 0.5f);
        var input = file.Write();

        var output = HostAtmosphere.RemoveHostEffects(input);
        var read = GPARAM.Read(output);
        object[] Values(string name) => read.Groups.SelectMany(g => g.Params).Single(p => p.Name1 == name).Values.ToArray();
        if (!Values("GS Presents - Chromatic Aberration Enable").SequenceEqual(new object[] { 0 })
            || !Values("GS Presents - Vignetting Power").SequenceEqual(new object[] { 0f, 0f })
            || !Values("VolumetricFog Enable").SequenceEqual(new object[] { false })
            || !Values("Authored Exposure").SequenceEqual(new object[] { 1.25f }) || !Values("VolumetricFog Authored Density").SequenceEqual(new object[] { 0.5f }))
            throw new Exception("Host screen and air effects were not switched off exactly");
        if (!output.SequenceEqual(HostAtmosphere.RemoveHostEffects(input)) || !HostAtmosphere.RemoveHostEffects(output).SequenceEqual(output))
            throw new Exception("Host effect removal does not repeat");
        foreach (var (group, name) in new[] { (tone, "GS Presents - Vignetting Power"), (air, "VolumetricFog Enable") })
        {
            var missing = GPARAM.Read(input);
            missing.Groups.Single(g => g.Name1 == group.Name1).Params.RemoveAll(p => p.Name1 == name);
            try { HostAtmosphere.RemoveHostEffects(missing.Write()); }
            catch (InvalidDataException) { continue; }
            throw new Exception("A missing host effect setting was accepted");
        }
        Console.WriteLine("PASS: host vignette, chromatic aberration and volumetric fog are switched off; other settings stay; missing settings fail");

        // The host's directional lights on characters: each colour's fourth value is its strength, kept per light set and time of day.
        var lights = new GPARAM { Game = GPARAM.GPGame.Sekiro, Groups = [], Unk3s = [], UnkBlock2 = [] };
        var set = new GPARAM.Group("Authored Lights", "lights") { Params = [], Comments = [] };
        lights.Groups.Add(set);
        string[] colours = ["Directional Light DiffColor0", "Directional Light SpecColor0", "Directional Light DiffColor1", "Directional Light SpecColor1",
            "Directional Light LocalLight DiffColor1", "Directional Light LocalLight SpecColor1", "Directional Light LocalLight DiffColor2", "Directional Light LocalLight SpecColor2"];
        foreach (string colour in colours) Add(set, colour, GPARAM.ParamType.Float4, new System.Numerics.Vector4(1, 0.9f, 0.8f, 0.35f), new System.Numerics.Vector4(0.5f, 0.6f, 0.7f, 2f));
        Add(set, "Directional Light Angle0", GPARAM.ParamType.Float2, new System.Numerics.Vector2(-0.6f, 0.7f));
        Add(set, "Directional Light Hemi Color Up", GPARAM.ParamType.Float4, new System.Numerics.Vector4(0.9f, 0.9f, 0.9f, 0.1f));
        var dark = HostAtmosphere.RemoveDirectionalLights(lights.Write());
        var darkRead = GPARAM.Read(dark).Groups.Single().Params.ToDictionary(p => p.Name1, p => p.Values);
        if (colours.Any(c => !darkRead[c].SequenceEqual(new object[] { new System.Numerics.Vector4(1, 0.9f, 0.8f, 0), new System.Numerics.Vector4(0.5f, 0.6f, 0.7f, 0) }))
            || !darkRead["Directional Light Angle0"].SequenceEqual(new object[] { new System.Numerics.Vector2(-0.6f, 0.7f) })
            || !darkRead["Directional Light Hemi Color Up"].SequenceEqual(new object[] { new System.Numerics.Vector4(0.9f, 0.9f, 0.9f, 0.1f) }))
            throw new Exception("The host's directional lights were not put out exactly");
        if (!HostAtmosphere.RemoveDirectionalLights(dark).SequenceEqual(dark)) throw new Exception("Putting out the host's lights does not repeat");
        var unlit = GPARAM.Read(lights.Write());
        unlit.Groups.Single().Params.RemoveAll(p => p.Name1 == "Directional Light LocalLight SpecColor2");
        try { HostAtmosphere.RemoveDirectionalLights(unlit.Write()); throw new Exception("A missing host light was accepted"); } catch (InvalidDataException) { }
        Console.WriteLine("PASS: the host's directional lights are put out at every time of day; their colours, angles and other settings stay");
    }
}
