// Spike: can we open Elden Ring's archives from the player's own install alone?
// Keys are read from the player's executable at run time and never printed or stored.
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

var game = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("AOG_ELDEN_RING")
    ?? throw new ArgumentException("pass the Elden Ring Game directory or set AOG_ELDEN_RING");
var paths = args.Skip(1).ToArray();

var exe = File.ReadAllBytes(Path.Combine(game, "eldenring.exe"));
var keys = PemKeys(Encoding.Latin1.GetString(exe)).ToList();
Console.WriteLine($"public keys found in executable: {keys.Count}");

var archives = Directory.GetFiles(game, "*.bhd").OrderBy(p => p).ToList();
var index = new Dictionary<ulong, (string Bdt, Entry Entry)>();
foreach (var bhd in archives)
{
    var cipher = File.ReadAllBytes(bhd);
    var (plain, keyNo) = keys.Select((k, i) => (Decrypt(cipher, k), i)).First(t => t.Item1.Length >= 4 && t.Item1.AsSpan(0, 4).SequenceEqual("BHD5"u8));
    var entries = ReadBhd5(plain).ToList();
    Console.WriteLine($"{Path.GetFileName(bhd)}: key #{keyNo}, {entries.Count} files");
    foreach (var e in entries) index[e.Hash] = (Path.ChangeExtension(bhd, ".bdt"), e);
}

byte[] Get(string path) =>
    index.TryGetValue(Hash(path), out var hit) ? ReadFile(hit.Bdt, hit.Entry) : throw new FileNotFoundException(path);

if (paths.Length > 0 && paths[0] == "floortest")
{
    // Every high-detail collision part in the Chapel of Anticipation becomes our floor and ramp.
    SoulsFormats.Oodle.SearchPaths.Add(game);
    var outDir = paths[1];
    BxfIn("m18_00_00_00", out var tmplBxf);
    var tmplSer = Serializer(tmplBxf);
    var template = tmplBxf.Files.First(f => f.Name.EndsWith("_900000.hkx.dcx")).Bytes;

    var spawn = SoulsFormats.MSBE.Read(Get("map/mapstudio/m10_01_00_00.msb.dcx")).Parts.Players.Single(p => p.Name == "c0000_0000").Position;
    float y0 = spawn.Y - 0.1f, y1 = y0 + 5;
    System.Numerics.Vector3 V(float dx, float y, float dz) => new(spawn.X + dx, y, spawn.Z + dz);
    var verts = new[] { V(-20, y0, -20), V(20, y0, -20), V(20, y0, 20), V(-20, y0, 20),   // floor, 40 m square
                        V(-2, y0, 3), V(2, y0, 3), V(2, y1, 13), V(-2, y1, 13) };          // ramp, 4 m wide, rising 5 m over 10 m
    var tris = new[] { (0, 3, 2), (0, 2, 1), (4, 7, 6), (4, 6, 5) };
    foreach (var (a, b, c) in tris)
        if (System.Numerics.Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).Y <= 0) throw new InvalidOperationException("triangle faces down");

    var chapel = BxfIn("m10_01_00_00", out var bxf);
    var writer = new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer();
    foreach (var f in bxf.Files.Where(f => !f.Name.EndsWith(".compendium.dcx")))
    {
        var root = tmplSer.Read(new MemoryStream(SoulsFormats.DCX.Decompress(template)));
        var id = System.Text.RegularExpressions.Regex.Match(f.Name, @"_(\d{6})\.hkx").Groups[1].Value;
        Build.ReplaceGeometry(root, verts, tris, "h" + id);
        var ms = new MemoryStream();
        writer.Write(root, ms);
        var hkx = ms.ToArray();
        var back = new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer().Read(new MemoryStream(hkx));
        Console.Write($"  {Path.GetFileName(f.Name.Replace('\\', '/'))}: {hkx.Length} bytes, reads back as");
        Summary(back);
        SoulsFormats.DCX.Decompress(f.Bytes, out var dcx);
        f.Bytes = SoulsFormats.DCX.Compress(hkx, dcx);
    }
    var mapDir = Path.Combine(outDir, "mod", "map", "m10", "m10_01_00_00");
    Directory.CreateDirectory(mapDir);
    bxf.Write(Path.Combine(mapDir, "h10_01_00_00.hkxbhd"), Path.Combine(mapDir, "h10_01_00_00.hkxbdt"));
    File.WriteAllText(Path.Combine(outDir, "age-of-gold-floortest.me3"), """
        profileVersion = "v1"
        savefile = "AgeOfGoldSpike.sl2"

        [[supports]]
        game = "eldenring"

        [[packages]]
        path = 'mod/'
        """);
    Console.WriteLine($"wrote {mapDir}");
    return;
}

SoulsFormats.BXF4 BxfIn(string map, out SoulsFormats.BXF4 bxf)
{
    var dir = $"map/{map[..3]}/{map}";
    return bxf = SoulsFormats.BXF4.Read(Get($"{dir}/h{map[1..]}.hkxbhd"), Get($"{dir}/h{map[1..]}.hkxbdt"));
}

static HKLib.Serialization.hk2018.Binary.HavokBinarySerializer Serializer(SoulsFormats.BXF4 bxf)
{
    var s = new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer();
    s.LoadCompendium(new MemoryStream(SoulsFormats.DCX.Decompress(bxf.Files.First(f => f.Name.EndsWith(".compendium.dcx")).Bytes)));
    return s;
}

if (paths.Length > 0 && paths[0] == "msb")
{
    SoulsFormats.Oodle.SearchPaths.Add(game);
    var msb = SoulsFormats.MSBE.Read(Get($"map/mapstudio/{paths[1]}.msb.dcx"));
    Console.WriteLine($"players {msb.Parts.Players.Count}  collisions {msb.Parts.Collisions.Count}  mapPieces {msb.Parts.MapPieces.Count}  assets {msb.Parts.Assets.Count}  enemies {msb.Parts.Enemies.Count}");
    foreach (var pl in msb.Parts.Players) Console.WriteLine($"  player {pl.Name} model {pl.ModelName} at {pl.Position} rot {pl.Rotation}");
    foreach (var c in msb.Parts.Collisions.Where(c => paths.Length < 3 || c.ModelName.Contains(paths[2])))
        Console.WriteLine($"  collision {c.Name} model {c.ModelName} at {c.Position} rot {c.Rotation}");
    return;
}

if (paths.Length > 0 && paths[0] == "collision")
{
    SoulsFormats.Oodle.SearchPaths.Add(game);
    var map = paths[1];                       // e.g. m18_00_00_00
    var dir = $"map/{map[..3]}/{map}";
    var outDir = Path.Combine(paths.Length > 2 ? paths[2] : ".", map);
    Directory.CreateDirectory(outDir);
    var bxf = SoulsFormats.BXF4.Read(Get($"{dir}/h{map[1..]}.hkxbhd"), Get($"{dir}/h{map[1..]}.hkxbdt"));
    Console.WriteLine($"h{map[1..]}: {bxf.Files.Count} files");
    var serializer = new HKLib.Serialization.hk2018.Binary.HavokBinarySerializer();
    var comp = bxf.Files.First(f => f.Name.EndsWith(".compendium.dcx"));
    serializer.LoadCompendium(new MemoryStream(SoulsFormats.DCX.Decompress(comp.Bytes)));
    foreach (var f in bxf.Files.Where(f => f != comp).Take(int.Parse(Environment.GetEnvironmentVariable("AOG_TAKE") ?? "41")))
    {
        var raw = SoulsFormats.DCX.Decompress(f.Bytes, out var dcx);
        var name = Path.GetFileName(f.Name.Replace('\\', '/'));
        File.WriteAllBytes(Path.Combine(outDir, name.Replace(".dcx", "")), raw);
        Console.WriteLine($"  {name}  {dcx}  {f.Bytes.Length} -> {raw.Length} bytes  magic '{System.Text.Encoding.ASCII.GetString(raw, 4, 4)}'");
        try
        {
            var compendium = serializer.ReadAllObjects(new MemoryStream(raw));
            if (Environment.GetEnvironmentVariable("AOG_SUMMARY") is not null) { Summary(compendium.First()); continue; }
            if (Environment.GetEnvironmentVariable("AOG_INSPECT") is { } want && name.Contains(want)) Inspect.Dump(compendium.First(), "    ");
            else foreach (var o in compendium.Take(12)) Console.WriteLine($"    {o.GetType().Name}");
            if (Environment.GetEnvironmentVariable("AOG_DUMP") is { } dump && name.Contains(dump)) new HKLib.Serialization.hk2018.Xml.HavokXmlSerializer().Write(compendium.First(), Path.Combine(outDir, name.Replace(".hkx.dcx", ".xml")));
        }
        catch (Exception ex) { Console.WriteLine($"    HKLib: {ex.GetType().Name}: {ex.Message}"); }
    }
    return;
}

foreach (var p in paths)
{
    if (!index.TryGetValue(Hash(p), out var hit)) { Console.WriteLine($"MISS {p}"); continue; }
    var data = ReadFile(hit.Bdt, hit.Entry);
    var magic = Encoding.ASCII.GetString(data, 0, 4).Replace('\0', '.');
    Console.WriteLine($"HIT  {p}  in {Path.GetFileName(hit.Bdt)}  {hit.Entry.Size} bytes  magic '{magic}'");
}

static void Summary(HKLib.hk2018.IHavokObject root)
{
    dynamic scene = ((HKLib.hk2018.hkRootLevelContainer)root).m_namedVariants[0].m_variant!;
    dynamic body = scene.m_systemDatas[0].m_bodyCinfos[0];
    dynamic shape = body.m_shape;
    dynamic tree = shape.m_data.m_meshTree;
    int sections = tree.m_sections.Count;
    int maxSecNodes = 0; foreach (var s in tree.m_sections) maxSecNodes = Math.Max(maxSecNodes, (int)s.m_nodes.Count);
    Console.WriteLine($"    bodies {scene.m_systemDatas[0].m_bodyCinfos.Count}  sections {sections}  prims {tree.m_primitives.Count}  packed {tree.m_packedVertices.Count}  shared {tree.m_sharedVertices.Count}  topNodes {tree.m_nodes.Count}  maxSectionNodes {maxSecNodes}  simd {shape.m_data.m_hasSimdTree}  tris {shape.m_pParam.m_triangleDataArray.Count}");
}

static IEnumerable<RSAParameters> PemKeys(string text)
{
    const string begin = "-----BEGIN RSA PUBLIC KEY-----", end = "-----END RSA PUBLIC KEY-----";
    for (int at = text.IndexOf(begin); at >= 0; at = text.IndexOf(begin, at + 1))
    {
        // Parsed with the managed ASN.1 reader: Wine's platform RSA import is unreliable.
        var body = text[(at + begin.Length)..text.IndexOf(end, at)];
        var der = Convert.FromBase64String(string.Concat(body.Where(c => !char.IsWhiteSpace(c))));
        var seq = new System.Formats.Asn1.AsnReader(der, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
        var n = seq.ReadIntegerBytes().ToArray().SkipWhile(b => b == 0).ToArray();
        var e = seq.ReadIntegerBytes().ToArray();
        yield return new RSAParameters { Modulus = n, Exponent = e };
    }
}

// Textbook RSA with the public key, block by block; each plaintext block is one byte shorter.
static byte[] Decrypt(byte[] cipher, RSAParameters key)
{
    int size = key.Modulus!.Length;
    var n = new BigInteger(key.Modulus, true, true);
    var e = new BigInteger(key.Exponent!, true, true);
    var output = new MemoryStream();
    for (int at = 0; at + size <= cipher.Length; at += size)
    {
        var m = BigInteger.ModPow(new BigInteger(cipher.AsSpan(at, size), true, true), e, n);
        var block = new byte[size - 1];
        var bytes = m.ToByteArray(true, true);
        if (bytes.Length > block.Length) return []; // wrong key
        bytes.CopyTo(block, block.Length - bytes.Length);
        output.Write(block);
    }
    return output.ToArray();
}

static IEnumerable<Entry> ReadBhd5(byte[] b)
{
    int I32(long o) => BitConverter.ToInt32(b, (int)o);
    long I64(long o) => BitConverter.ToInt64(b, (int)o);
    long buckets = I32(0x10), bucketsAt = I32(0x14);
    for (long i = 0; i < buckets; i++)
    {
        long at = bucketsAt + i * 8;
        int count = I32(at);
        long headers = I32(at + 4);
        for (int j = 0; j < count; j++)
        {
            long h = headers + j * 40;
            long keyAt = I64(h + 32);
            byte[]? aes = null; var ranges = new List<(long, long)>();
            if (keyAt != 0)
            {
                aes = b[(int)keyAt..(int)(keyAt + 16)];
                int rc = I32(keyAt + 16);
                for (int r = 0; r < rc; r++) ranges.Add((I64(keyAt + 20 + r * 16), I64(keyAt + 28 + r * 16)));
            }
            yield return new Entry(BitConverter.ToUInt64(b, (int)h), I32(h + 8), I64(h + 16), aes, ranges);
        }
    }
}

static byte[] ReadFile(string bdt, Entry e)
{
    using var f = File.OpenRead(bdt);
    var bytes = new byte[e.Size];
    f.Position = e.Offset;
    f.ReadExactly(bytes);
    if (e.Aes is null) return bytes;
    using var aes = Aes.Create();
    aes.Key = e.Aes;
    foreach (var (start, stop) in e.Ranges.Where(r => r.Item1 >= 0 && r.Item2 > r.Item1))
        aes.DecryptEcb(bytes.AsSpan((int)start, (int)(stop - start)), bytes.AsSpan((int)start), PaddingMode.None);
    return bytes;
}

// Elden Ring names files by a 64-bit polynomial hash of the lower-case path.
static ulong Hash(string path)
{
    ulong h = 0;
    foreach (char c in "/" + path.ToLowerInvariant().Replace('\\', '/').TrimStart('/')) h = h * 0x85 + c;
    return h;
}

record Entry(ulong Hash, int Size, long Offset, byte[]? Aes, List<(long, long)> Ranges);
