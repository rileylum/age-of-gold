using System.Buffers.Binary;
using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>Reads how the source dresses a human character: the part models of what it wears and holds.</summary>
internal static class SourceHumanReader
{
    /// <param name="Parts">The part models of the body, by file stem: what is worn on head, body, arms and legs, then the head itself.</param>
    /// <param name="Right">The part model held in the right hand, if any; <paramref name="Left"/> likewise.</param>
    internal sealed record Human(string[] Parts, string? Right, string? Left);

    // The source's tables by the row length this reader knows, and where a row keeps what is read. A start-up row lists
    // two right-hand and two left-hand weapons, then what is worn on head, body, arms and legs; an equipment row names
    // its part model by number.
    private const int StartSize = 240, RightHand = 16, LeftHand = 24, Worn = 32;
    private const int ProtectorSize = 232, ProtectorModel = 160, WeaponSize = 272, WeaponModel = 184;
    private static readonly string[] Slots = ["HD", "BD", "AM", "LG"];
    // The head model is the skull and neck. The front of the face is not here: the source generates it for each character
    // as the game runs, from shaping data this reader does not translate.
    private static readonly string[] Head = ["FC_M_0000"];

    // lazy: a male body with no face front and no hair; the source generates each face and picks hair from parameters
    // that are not read, and its female part models are not chosen.
    internal static Human Read(Install source, int startRow)
    {
        var bundle = BND3.Read(source.Read("param/GameParam/GameParam.parambnd.dcx"));
        Dictionary<int, byte[]> Table(string name, string type, int size)
        {
            var file = bundle.Files.SingleOrDefault(f => f.Name.Replace('\\', '/').EndsWith($"/{name}.param", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Source install has no table " + name);
            var table = PARAM.Read(file.Bytes);
            if (table.ParamType != type || table.DetectedSize != size || table.BigEndian) throw new InvalidDataException($"Unsupported source table {name}: {table.ParamType}, row size {table.DetectedSize}");
            var definition = new PARAMDEF { ParamType = table.ParamType, DataVersion = table.ParamdefDataVersion };
            definition.Fields.Add(new PARAMDEF.Field(definition, PARAMDEF.DefType.dummy8, "raw") { ArrayLength = size });
            table.ApplyParamdef(definition);
            return table.Rows.GroupBy(r => r.ID).ToDictionary(g => g.Key, g => g.Count() == 1 ? (byte[])g.Single()["raw"].Value : throw new InvalidDataException($"Source table {name} repeats row {g.Key}"));
        }
        if (!Table("CharaInitParam", "CHARACTER_INIT_PARAM", StartSize).TryGetValue(startRow, out var start)) throw new InvalidDataException("Source has no start-up row " + startRow);
        var protectors = Table("EquipParamProtector", "EQUIP_PARAM_PROTECTOR_ST", ProtectorSize); var weapons = Table("EquipParamWeapon", "EQUIP_PARAM_WEAPON_ST", WeaponSize);
        // An item's last two digits count its reinforcement; the row of the unreinforced item names the model.
        static int Model(Dictionary<int, byte[]> rows, int item, int at, string what)
        {
            var row = rows.TryGetValue(item, out var exact) ? exact : rows.TryGetValue(item - item % 100, out var plain) ? plain : throw new InvalidDataException($"Source has no {what} row for {item}");
            int model = BinaryPrimitives.ReadInt16LittleEndian(row.AsSpan(at));
            return model is >= 0 and <= 9999 ? model : throw new InvalidDataException($"Source {what} {item} names no part model");
        }
        var parts = Slots.Select((slot, i) =>
        {
            int item = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(Worn + 4 * i));
            // An empty slot shows the bare body part.
            return $"{slot}_M_{(item > 0 ? Model(protectors, item, ProtectorModel, "armour") : 0):D4}";
        }).Concat(Head).ToArray();
        string? Held(int at)
        {
            int item = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(at));
            return item > 0 ? $"WP_A_{Model(weapons, item, WeaponModel, "weapon"):D4}" : null;
        }
        return new(parts, Held(RightHand), Held(LeftHand));
    }
}
