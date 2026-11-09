using System.Reflection.PortableExecutable;
using System.Text;
using AgeOfGold.Runtime;

namespace AgeOfGold.Shaders;

internal sealed record SourceRendererSettings(float HemisphereStrength, float NormalStrength)
{
    internal static SourceRendererSettings Read(Install source) => Read(new ExecutableImage(source.Read("DarkSoulsRemastered.exe")));

    internal static SourceRendererSettings Read(ExecutableImage image)
    {
        if (image.Version != new Version(1, 3, 1, 0))
            throw new InvalidDataException("Unsupported source renderer version; expected 1.3.1.0");
        return new(Float("m_dbgAmbientStrength"), Float("m_dbgNormalStrength"));

        float Float(string name)
        {
            var value = BitConverter.UInt32BitsToSingle(image.U32(image.Raw(Storage(image, name, 2), 4)));
            if (!float.IsFinite(value) || value < 0)
                throw new InvalidDataException($"Invalid source renderer setting value: {name}");
            return value;
        }
    }

    internal static int Storage(ExecutableImage image, string name, uint kind)
    {
        var encoded = Encoding.Unicode.GetBytes(name + "\0");
        var matches = image.Find(encoded).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Source renderer setting {name} is missing or ambiguous");
        var address = matches[0];
        if (address % 8 != 0 || !image.Has(address, 112, SectionCharacteristics.MemRead, SectionCharacteristics.MemExecute))
            throw new InvalidDataException($"Unsupported source renderer setting record: {name}");
        var at = image.Raw(address, 112);
        // The installed setting catalogue stores a padded UTF-16 name, scalar kind and relocated pointer.
        if (image.Bytes.AsSpan(at + encoded.Length, 100 - encoded.Length).IndexOfAnyExcept((byte)0) >= 0
            || image.U32(at + 100) != kind)
            throw new InvalidDataException($"Unsupported source renderer setting record: {name}");
        var pointer = image.U64(at + 104);
        var size = kind == 3 ? 1 : 4;
        if (pointer < image.Base || pointer - image.Base >= image.Size || pointer % (uint)size != 0
            || !image.Has((int)(pointer - image.Base), size, SectionCharacteristics.MemRead | SectionCharacteristics.MemWrite,
                SectionCharacteristics.MemExecute))
            throw new InvalidDataException($"Invalid source renderer setting storage: {name}");
        return (int)(pointer - image.Base);
    }
}
