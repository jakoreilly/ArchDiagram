// Ported from ArchForge\archdiagram\Program.cs (ProjectScanner / SourceTextScanner /
// ConnectionStringNormalizer), extended with: PackageReference + TargetFramework
// extraction, connection-string variable-name capture, and server extraction so
// database nodes get human-readable labels instead of hashes.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ArchDiagram.Graph;

namespace ArchDiagram.Scanning;

public static class CsprojScanner
{
    public static List<CsprojInfo> Scan(string root, IReadOnlyList<FileEntry> files, List<string> diagnostics)
    {
        var csprojFiles = files.Where(f => f.Extension == ".csproj")
            .OrderBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // First pass: full path -> project name, so reference resolution can map paths to names.
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in csprojFiles)
        {
            byPath[Path.GetFullPath(f.AbsPath)] = Path.GetFileNameWithoutExtension(f.AbsPath);
        }

        // Prefix (root-relative folder) of each project, longest first, so a .cs file nested
        // under two projects is attributed only to the nearest-enclosing one (B10).
        var projectPrefixes = csprojFiles
            .Select(f =>
            {
                var prefix = Path.GetRelativePath(root, Path.GetDirectoryName(f.AbsPath)!).Replace('\\', '/');
                return (Id: f.RelPath, Prefix: prefix == "." ? "" : prefix);
            })
            .OrderByDescending(p => p.Prefix.Length)
            .ToList();

        string? OwningProject(string csRelPath) => projectPrefixes
            .FirstOrDefault(p => p.Prefix.Length == 0 || csRelPath.StartsWith(p.Prefix + "/", StringComparison.OrdinalIgnoreCase))
            .Id;

        var results = new List<CsprojInfo>();
        foreach (var f in csprojFiles)
        {
            var name = Path.GetFileNameWithoutExtension(f.AbsPath);
            var dir = Path.GetDirectoryName(f.AbsPath)!;

            var refs = new List<string>();
            var packages = new List<string>();
            var tfm = "";
            try
            {
                var xml = XDocument.Load(f.AbsPath);
                foreach (var el in xml.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
                {
                    var include = (string?)el.Attribute("Include");
                    if (string.IsNullOrWhiteSpace(include)) { continue; }
                    var resolved = Path.GetFullPath(Path.Combine(dir, include.Replace('\\', Path.DirectorySeparatorChar)));
                    refs.Add(byPath.TryGetValue(resolved, out var refName) ? refName : Path.GetFileNameWithoutExtension(resolved));
                }
                foreach (var el in xml.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
                {
                    var include = (string?)el.Attribute("Include");
                    if (!string.IsNullOrWhiteSpace(include)) { packages.Add(include); }
                }
                tfm = xml.Descendants().FirstOrDefault(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")?.Value ?? "";
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
            {
                diagnostics.Add($"Could not parse {f.RelPath}: {ex.Message}");
            }

            // Connection strings from .cs files this project owns (nearest-enclosing, B10).
            var dbUses = new List<DbUse>();
            foreach (var cs in files.Where(x => x.Extension == ".cs" &&
                         string.Equals(OwningProject(x.RelPath), f.RelPath, StringComparison.OrdinalIgnoreCase)))
            {
                string text;
                try { text = File.ReadAllText(cs.AbsPath); }
                catch (IOException) { continue; }

                foreach (var hit in SourceTextScanner.FindConnectionStringLiterals(text))
                {
                    dbUses.Add(BuildDbUse(hit, $"{cs.RelPath}:{hit.LineNumber}"));
                }
            }

            results.Add(new CsprojInfo
            {
                Name = name,
                RelPath = f.RelPath,
                TargetFramework = tfm,
                ProjectReferenceNames = refs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r, StringComparer.Ordinal).ToList(),
                PackageReferences = packages.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList(),
                ConnectionStrings = dbUses,
            });
        }

        return results;
    }

    /// <summary>Label priority: catalog/database name > assigned variable name > short hash.</summary>
    private static DbUse BuildDbUse(ConnectionStringHit hit, string evidence)
    {
        var hash = ConnectionStringNormalizer.NormalizeAndHash(hit.RawConnectionString);
        var catalog = ConnectionStringNormalizer.ExtractValue(hit.RawConnectionString, ["database", "initial catalog"]) ?? "";
        var server = ConnectionStringNormalizer.ExtractValue(hit.RawConnectionString, ["server", "data source", "host"]) ?? "";
        var label = !string.IsNullOrWhiteSpace(catalog) ? catalog
            : !string.IsNullOrWhiteSpace(hit.VariableName) ? hit.VariableName
            : "db-" + hash[..8];
        return new DbUse
        {
            Hash = hash,
            Label = label,
            Server = server,
            Catalog = catalog,
            VariableName = hit.VariableName,
            Evidence = evidence,
        };
    }

    /// <summary>Dedupe DbUses across all projects into one node per logical database.
    /// First human-readable label seen for a hash wins.</summary>
    public static List<DbNode> BuildDbNodes(IEnumerable<CsprojInfo> projects)
    {
        var nodes = new Dictionary<string, DbNode>(StringComparer.Ordinal);
        foreach (var use in projects.SelectMany(p => p.ConnectionStrings))
        {
            if (!nodes.TryGetValue(use.Hash, out var existing) ||
                (existing.Label.StartsWith("db-", StringComparison.Ordinal) && !use.Label.StartsWith("db-", StringComparison.Ordinal)))
            {
                nodes[use.Hash] = new DbNode { Hash = use.Hash, Label = use.Label, Server = use.Server, Catalog = use.Catalog };
            }
        }
        return nodes.Values.OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Hash, StringComparer.Ordinal).ToList();
    }
}

public sealed record ConnectionStringHit(string RawConnectionString, int LineNumber, string VariableName);

/// <summary>Cheap text scanning for connection-string-shaped literals (ported from
/// the ArchForge prototype). Additionally captures the identifier the literal is
/// assigned to (variable/property/config key) for human-readable labeling.</summary>
public static class SourceTextScanner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex ConnectionStringLiteral = new(
        "\"((?:[A-Za-z][A-Za-z0-9 _]*=[^\";]+;){1,}[A-Za-z][A-Za-z0-9 _]*=[^\";]+;?)\"",
        RegexOptions.Compiled, Timeout);

    private static readonly Regex ConnectionStringHintKey = new(
        @"(?i)\b(server|data source|host|database|initial catalog|user id|uid|password|pwd)\s*=",
        RegexOptions.Compiled, Timeout);

    // `var ordersDb = "..."` / `OrdersConnection = "..."` / `"OrdersDb": "..."`
    private static readonly Regex AssignmentTarget = new(
        "(?:([A-Za-z_][A-Za-z0-9_]*)\\s*=|\"([A-Za-z_][A-Za-z0-9_.:]*)\"\\s*:)\\s*$",
        RegexOptions.Compiled, Timeout);

    public static IEnumerable<ConnectionStringHit> FindConnectionStringLiterals(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            MatchCollection matches;
            try { matches = ConnectionStringLiteral.Matches(lines[i]); }
            catch (RegexMatchTimeoutException) { continue; }

            foreach (Match m in matches)
            {
                var candidate = m.Groups[1].Value;
                bool hasHint;
                try { hasHint = ConnectionStringHintKey.IsMatch(candidate); }
                catch (RegexMatchTimeoutException) { hasHint = false; }
                if (!hasHint) { continue; }

                var varName = "";
                try
                {
                    var before = lines[i][..m.Index];
                    var t = AssignmentTarget.Match(before);
                    if (t.Success) { varName = t.Groups[1].Success ? t.Groups[1].Value : t.Groups[2].Value; }
                }
                catch (RegexMatchTimeoutException) { /* label falls back to hash */ }

                yield return new ConnectionStringHit(candidate, i + 1, varName);
            }
        }
    }
}

/// <summary>Normalizes + hashes a connection string (same recipe as ArchForge) so the
/// same logical database referenced from multiple projects collapses to one node.
/// The hash is metadata only — labels always come from catalog/variable names.</summary>
public static class ConnectionStringNormalizer
{
    private static readonly string[] SecretKeys = ["password", "pwd", "user id", "uid"];

    public static string NormalizeAndHash(string connectionString)
    {
        var pairs = SplitPairs(connectionString)
            .Where(p => !SecretKeys.Contains(p.Key.ToLowerInvariant()))
            .Select(p => ($"{p.Key.ToLowerInvariant()}={p.Value.ToLowerInvariant()}"))
            .OrderBy(p => p, StringComparer.Ordinal);
        var normalized = string.Join(';', pairs);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Returns the first non-empty value for any of the given keys
    /// (case-insensitive), original casing preserved, or null.</summary>
    public static string? ExtractValue(string connectionString, string[] keys)
    {
        foreach (var (key, value) in SplitPairs(connectionString))
        {
            if (keys.Contains(key.ToLowerInvariant()) && !string.IsNullOrWhiteSpace(value)) { return value; }
        }
        return null;
    }

    private static IEnumerable<(string Key, string Value)> SplitPairs(string connectionString) =>
        connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SplitPair)
            .Where(p => p is not null)
            .Select(p => p!.Value);

    private static (string Key, string Value)? SplitPair(string segment)
    {
        var idx = segment.IndexOf('=');
        if (idx < 0) { return null; }
        return (segment[..idx].Trim(), segment[(idx + 1)..].Trim());
    }
}
