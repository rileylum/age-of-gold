using HKLib.hk2018;
using HKLib.Reflection.hk2018;
using HKLib.Serialization.hk2018.Binary;

namespace AgeOfGold.Collision;

/// <summary>
/// The type descriptions of one host Havok file, and files written with them. The Havok library can write only a type
/// whose description and identity it has read from a host file, so whatever is written starts from a host exemplar that
/// uses every kind of object the new file needs.
/// </summary>
internal sealed class HostTagfile
{
    private readonly HavokTypeRegistry types;

    private HostTagfile(HavokTypeRegistry types) => this.types = types;

    /// <summary>The types of a host file that describes itself, and the objects it holds.</summary>
    internal static HostTagfile Read(byte[] file, out hkRootLevelContainer root)
    {
        var reader = new HavokBinarySerializer();
        root = reader.Read(new MemoryStream(file)) as hkRootLevelContainer ?? throw new InvalidDataException("Host Havok file has no root container");
        return new(reader.TypeRegistry);
    }

    /// <summary>The types of a table that a binder's host files share instead of each describing itself.</summary>
    internal static HostTagfile ReadShared(byte[] table)
    {
        var reader = new HavokBinarySerializer();
        reader.LoadCompendium(reader.ReadCompendium(new MemoryStream(table)));
        return new(reader.TypeRegistry);
    }

    /// <summary>A file that describes itself, holding the root's objects.</summary>
    internal byte[] Write(hkRootLevelContainer root)
    {
        using var stream = new MemoryStream();
        new HavokBinarySerializer(types).Write(root, stream);
        return stream.ToArray();
    }
}
