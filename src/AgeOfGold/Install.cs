using System.Security.Cryptography;

namespace AgeOfGold;

internal sealed class Install
{
    internal string Root { get; }
    internal SortedDictionary<string, string> Fingerprints { get; } = new(StringComparer.Ordinal);

    internal Install(string root, string executable)
    {
        Root = Canonical(root);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Install not found: {Root}");
        Read(executable);
    }

    internal string Locate(string relative)
    {
        var path = Canonical(Path.Combine(Root, Relative(relative)));
        if (!Within(path, Root)) throw new IOException($"Install path escapes its root: {relative}");
        return path;
    }

    internal byte[] Read(string relative)
    {
        var bytes = File.ReadAllBytes(Locate(relative));
        Fingerprints[Relative(relative)] = Convert.ToHexString(SHA256.HashData(bytes));
        return bytes;
    }

    internal void Fingerprint(string relative)
    {
        using var stream = File.OpenRead(Locate(relative));
        Fingerprints[Relative(relative)] = Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static string Relative(string path)
    {
        var value = path.Replace('\\', '/');
        if (value.Length == 0 || value.Split('/').Any(p => p is "" or "." or "..")
            || value.Any(c => char.IsControl(c) || ":*?\"<>|".Contains(c)))
            throw new ArgumentException($"Expected a relative file path: {path}");
        return value;
    }

    internal static bool Within(string path, string root) => path.Equals(root, Comparison)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, Comparison);

    private static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // Resolve every existing ancestor: a fresh output below a symlink can still enter an install.
    internal static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(true)!.FullName;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }
}
