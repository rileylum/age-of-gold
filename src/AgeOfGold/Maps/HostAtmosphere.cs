using AgeOfGold.Params;
using SoulsFormats;

namespace AgeOfGold.Maps;

/// <summary>Adjusts the host map's graphics parameters where its screen and air effects have no source counterpart.</summary>
internal static class HostAtmosphere
{
    /// <summary>Switches off the host's vignette, chromatic aberration and volumetric fog, which the source game does not render.</summary>
    internal static byte[] RemoveHostEffects(byte[] parameters)
    {
        var file = GPARAM.Read(parameters);
        void Set(string name, Func<object, object> value) => HostAtmosphere.Set(file, name, value);
        Set("GS Presents - Chromatic Aberration Enable", v => v is int ? 0 : throw new InvalidDataException("Unsupported chromatic aberration setting"));
        Set("GS Presents - Vignetting Power", v => v is float ? 0f : throw new InvalidDataException("Unsupported vignetting setting"));
        Set("VolumetricFog Enable", v => v is bool ? false : throw new InvalidDataException("Unsupported volumetric fog setting"));
        return file.Write();
    }

    /// <summary>
    /// Gives the host lens the source row's far focus range. The source blurs by distance alone, so the host's
    /// focus on the camera target is switched off.
    /// </summary>
    // lazy: far range only; near blur and blur strength keep the host lens values until they are compared in play.
    internal static byte[] FocusLikeSource(byte[] parameters, SourceAtmosphereReader.Focus focus)
    {
        if (!(focus.FarStart >= 0 && focus.FarEnd > focus.FarStart)) throw new InvalidDataException("Source far focus range is empty or reversed");
        var file = GPARAM.Read(parameters);
        Set(file, "[GS]Lens Simulation Param FarStart", v => v is float ? focus.FarStart : throw new InvalidDataException("Unsupported lens setting"));
        Set(file, "[GS]Lens Simulation Param FarEnd", v => v is float ? focus.FarEnd : throw new InvalidDataException("Unsupported lens setting"));
        Set(file, "[GS]Lens Simulation Param UseCameraFocus", v => v is int ? 0 : throw new InvalidDataException("Unsupported lens setting"));
        return file.Write();
    }

    private static void Set(GPARAM file, string name, Func<object, object> value)
    {
        var matches = file.Groups.SelectMany(g => g.Params).Where(p => p.Name1 == name).ToArray();
        // A renamed or missing setting would leave the host behaviour in place without notice.
        if (matches.Length != 1 || matches[0].Values.Count == 0) throw new InvalidDataException("Host graphics setting not found exactly once: " + name);
        for (int i = 0; i < matches[0].Values.Count; i++) matches[0].Values[i] = value(matches[0].Values[i]);
    }
}
