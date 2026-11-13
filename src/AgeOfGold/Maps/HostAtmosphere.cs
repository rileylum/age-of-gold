using SoulsFormats;

namespace AgeOfGold.Maps;

/// <summary>Adjusts the host map's graphics parameters where its screen and air effects have no source counterpart.</summary>
internal static class HostAtmosphere
{
    /// <summary>Switches off the host's vignette, chromatic aberration and volumetric fog, which the source game does not render.</summary>
    internal static byte[] RemoveHostEffects(byte[] parameters)
    {
        var file = GPARAM.Read(parameters);
        void Set(string name, Func<object, object> value)
        {
            var matches = file.Groups.SelectMany(g => g.Params).Where(p => p.Name1 == name).ToArray();
            // A renamed or missing setting would leave the host effect on without notice.
            if (matches.Length != 1 || matches[0].Values.Count == 0) throw new InvalidDataException("Host graphics setting not found exactly once: " + name);
            for (int i = 0; i < matches[0].Values.Count; i++) matches[0].Values[i] = value(matches[0].Values[i]);
        }
        Set("GS Presents - Chromatic Aberration Enable", v => v is int ? 0 : throw new InvalidDataException("Unsupported chromatic aberration setting"));
        Set("GS Presents - Vignetting Power", v => v is float ? 0f : throw new InvalidDataException("Unsupported vignetting setting"));
        Set("VolumetricFog Enable", v => v is bool ? false : throw new InvalidDataException("Unsupported volumetric fog setting"));
        return file.Write();
    }
}
