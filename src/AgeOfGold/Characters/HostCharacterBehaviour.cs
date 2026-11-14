using AgeOfGold.Collision;
using HKLib.hk2018;
using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>
/// Writes the behaviour files of a build-owned host character from an installed character's. A host enemy's behaviour is
/// a project file, a character file, a small graph of its own and the graph the host's enemies share, which plays
/// animations by number and takes its bone numbers from the character file — so it serves any skeleton.
/// </summary>
internal static class HostCharacterBehaviour
{
    /// <summary>The bones, by their place in the character's skeleton, that the host turns to look at something: along the neck and along the spine.</summary>
    internal sealed record Gaze(int NeckFrom, int NeckTo, int SpineFrom, int SpineTo);

    /// <param name="exemplarBinder">An installed host enemy's behaviour binder.</param>
    /// <param name="exemplar">That enemy's identity, as "c" and four digits.</param>
    /// <param name="gaze">Null for a body that does not turn to look.</param>
    internal static byte[] Write(byte[] exemplarBinder, string exemplar, string identity, Gaze? gaze)
    {
        var binder = BND4.Read(exemplarBinder);
        string Renamed(string name) => name.Replace(exemplar, identity, StringComparison.OrdinalIgnoreCase);
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in binder.Files)
        {
            string member = file.Name.Replace('\\', '/').Split('/')[^1];
            // The shared graph is the same for every enemy and keeps its name, under the new character's directory.
            if (member.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) && !member.StartsWith(exemplar, StringComparison.OrdinalIgnoreCase))
            {
                file.Name = Renamed(file.Name[..^member.Length]) + member; seen.Add("shared");
                continue;
            }
            if (!member.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unclassified behaviour binder member: " + member);
            var types = HostTagfile.Read(file.Bytes, out var root);
            switch (root.m_namedVariants.Single().m_variant)
            {
                case hkbProjectData { m_stringData: { } names }:
                    names.m_behaviorFilenames = names.m_behaviorFilenames.Select(n => Renamed(n!)).ToList()!;
                    names.m_characterFilenames = names.m_characterFilenames.Select(n => Renamed(n!)).ToList()!;
                    names.m_fullPathToSource = Renamed(names.m_fullPathToSource ?? "");
                    seen.Add("project");
                    break;
                case hkbCharacterData { m_stringData: { } names, m_characterPropertyValues: { } values } character:
                    void Set(string property, int value)
                    {
                        int at = names.m_characterPropertyNames.IndexOf(property);
                        if (at < 0 || at >= values.m_wordVariableValues.Count) throw new InvalidDataException("Host character file has no property " + property);
                        values.m_wordVariableValues[at].m_value = value;
                    }
                    Set("RefTwistEnable", gaze is null ? 0 : 1);
                    if (gaze is not null) { Set("RefTwistNeckStart", gaze.NeckFrom); Set("RefTwistNeckEnd", gaze.NeckTo); Set("RefTwistSpineStart", gaze.SpineFrom); Set("RefTwistSpineEnd", gaze.SpineTo); }
                    // The host plants feet through helper bones of its own skeletons; a source skeleton has none, and the
                    // source does not plant these characters' feet. Left in, the settings would bend whatever bones hold those places.
                    character.m_propertySheets.RemoveAll(sheet => sheet is hkbFootIkDriverInfo or hkbHandIkDriverInfo);
                    names.m_name = identity; names.m_rigName = Renamed(names.m_rigName ?? ""); names.m_behaviorFilename = Renamed(names.m_behaviorFilename ?? "");
                    names.m_luaFiles = names.m_luaFiles.Select(n => Renamed(n!)).ToList()!;
                    seen.Add("character");
                    break;
                case hkbBehaviorGraph { m_rootGenerator: hkbStateMachine { m_states.Count: 1 } machine } graph when machine.m_states[0] is { m_generator: hkbModifierGenerator { m_generator: hkbBehaviorReferenceGenerator shared } } state:
                    // The character's own graph only wraps the shared one in foot and hand placement; it now plays it directly.
                    state.m_generator = shared;
                    machine.m_name = Renamed(machine.m_name ?? ""); graph.m_name = Renamed(graph.m_name ?? "");
                    seen.Add("graph");
                    break;
                default: throw new InvalidDataException("Unsupported host behaviour file: " + member);
            }
            file.Bytes = types.Write(root);
            file.Name = Renamed(file.Name);
        }
        if (!seen.SetEquals(["character", "graph", "project", "shared"])) throw new InvalidDataException("Host behaviour binder is not a project, a character, its graph and the shared graph");
        return binder.Write();
    }
}
