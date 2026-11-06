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
    foreach (var f in bxf.Files.Where(f => f != comp).Take(int.Parse(Environment.GetEnvironmentVariable("AOG_TAKE") ?? "3")))
    {
        var raw = SoulsFormats.DCX.Decompress(f.Bytes, out var dcx);
        var name = Path.GetFileName(f.Name.Replace('\\', '/'));
        File.WriteAllBytes(Path.Combine(outDir, name.Replace(".dcx", "")), raw);
        Console.WriteLine($"  {name}  {dcx}  {f.Bytes.Length} -> {raw.Length} bytes  magic '{System.Text.Encoding.ASCII.GetString(raw, 4, 4)}'");
        try
        {
            var compendium = serializer.ReadAllObjects(new MemoryStream(raw));
            foreach (var o in compendium.Take(12)) Console.WriteLine($"    {o.GetType().Name}");
            if (Environment.GetEnvironmentVariable("AOG_DUMP") is not null) new HKLib.Serialization.hk2018.Xml.HavokXmlSerializer().Write(compendium.First(), Path.Combine(outDir, name.Replace(".hkx.dcx", ".xml")));
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
