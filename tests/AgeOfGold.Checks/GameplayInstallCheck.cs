using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using AgeOfGold;
using AgeOfGold.Archives;
using AgeOfGold.Params;
using SoulsFormats;
using static GameplayTablesCheck;

internal static class GameplayInstallCheck
{
    internal static void Run(string output)
    {
        if (Environment.GetEnvironmentVariable("CI") is not null)
            throw new InvalidOperationException("Install checks must never run in CI");
        var source = new Install(Required("AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Required("AOG_ELDEN_RING"), "eldenring.exe");
        var map = MSB1.Read(source.Read("map/MapStudio/m10_02_00_00.msb"));
        var bonfire = map.Parts.Objects.Single(p => p.ModelName == "o0200");
        var executable = host.Read("eldenring.exe");
        var encrypted = host.Read("regulation.bin");
        var regulation = Regulation.Read(executable, encrypted);
        var original = regulation.Tables.Files.ToDictionary(f => f.ID, f => f.Bytes.ToArray());
        var action = regulation.Tables.Files.Single(f => f.Name.EndsWith("ActionButtonParam.param"));
        var resting = regulation.Tables.Files.Single(f => f.Name.EndsWith("BonfireWarpParam.param"));
        var actions = Read(action.Bytes, 100);
        var destinations = Read(resting.Bytes, 236);
        uint Word(PARAM.Row row, int at) => BinaryPrimitives.ReadUInt32LittleEndian(Payload(row).AsSpan(at));
        var actionTemplate = actions.Rows.First(r => BinaryPrimitives.ReadInt32LittleEndian(Payload(r).AsSpan(52)) > 0);
        var restTemplate = destinations.Rows.First(r => Word(r, 8) > 0 && destinations.Rows.Count(t => Word(t, 8) == Word(r, 8)) == 1);
        int Free(IEnumerable<int> occupied) => Enumerable.Range(9_100_000, 1000).First(id => !occupied.Contains(id));
        var actionId = Free(actions.Rows.Select(r => r.ID));
        var restId = Free(destinations.Rows.Select(r => r.ID));
        var entity = (uint)Free(destinations.Rows.Select(r => unchecked((int)Word(r, 8))));
        var flag = (uint)Free(destinations.Rows.Select(r => unchecked((int)Word(r, 4))));
        var destination = new GameplayTables.Destination(restId, "authored install check", entity, flag,
            10, 1, 0, bonfire.Position + new Vector3(0, 200, 0), actionId);
        byte[] EditAction() => GameplayTables.AddInteraction(original[action.ID], actionTemplate.ID, actionId, "authored install check", actionId);
        byte[] EditResting() => GameplayTables.ReplaceDestinations(original[resting.ID], Word(restTemplate, 8), destination);
        action.Bytes = EditAction(); resting.Bytes = EditResting();
        GameplayTablesCheck.Require(action.Bytes.SequenceEqual(EditAction()) && resting.Bytes.SequenceEqual(EditResting()), "Independent installed-table edits differ");
        var path = Path.Combine(output, "regulation.bin");
        File.WriteAllBytes(path, regulation.Write());
        var actual = Regulation.Read(executable, File.ReadAllBytes(path));
        var actualAction = Read(actual.Tables.Files.Single(f => f.ID == action.ID).Bytes, 100);
        var added = actualAction.Rows.Single(r => r.ID == actionId);
        var wantedAction = Payload(actionTemplate).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(wantedAction.AsSpan(52), actionId);
        GameplayTablesCheck.Require(Payload(added).SequenceEqual(wantedAction), "Installed interaction template fields changed");
        actualAction.Rows.Remove(added);
        GameplayTablesCheck.Require(actualAction.Write().SequenceEqual(original[action.ID]), "Installed interaction rows changed");
        var actualResting = Read(actual.Tables.Files.Single(f => f.ID == resting.ID).Bytes, 236);
        GameplayTablesCheck.Require(actualResting.Rows.Count == 1, "Host destinations survived replacement");
        var placed = Payload(actualResting.Rows[0]);
        GameplayTablesCheck.Require(new Vector3(BinaryPrimitives.ReadSingleLittleEndian(placed.AsSpan(36)),
                BinaryPrimitives.ReadSingleLittleEndian(placed.AsSpan(40)), BinaryPrimitives.ReadSingleLittleEndian(placed.AsSpan(44))) == destination.Position,
            "Source resting placement changed during encrypted readback");
        var retained = placed.ToArray();
        foreach (var (at, length) in new[] { (4, 8), (20, 4), (32, 3), (36, 16) })
            Payload(restTemplate).AsSpan(at, length).CopyTo(retained.AsSpan(at, length));
        GameplayTablesCheck.Require(retained.SequenceEqual(Payload(restTemplate)), "Unedited resting fields changed");
        actualResting.Rows = destinations.Rows;
        GameplayTablesCheck.Require(actualResting.Write().SequenceEqual(original[resting.ID]), "Resting table metadata changed");
        foreach (var file in actual.Tables.Files.Where(f => f.ID != action.ID && f.ID != resting.ID))
            GameplayTablesCheck.Require(file.Bytes.SequenceEqual(original[file.ID]), "An unrelated gameplay table changed");
        foreach (var install in new[] { source, host })
            foreach (var input in install.Fingerprints)
                GameplayTablesCheck.Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(install.Locate(input.Key)))) == input.Value, "Install input changed");
        Console.WriteLine($"PASS: installed tables edited twice and read back encrypted; source resting placement retained; "
            + $"{original.Count - 2} other tables and all install inputs unchanged");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException($"Set {name}");
}
