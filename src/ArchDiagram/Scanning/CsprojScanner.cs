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

            // Connection strings from the files this project owns (nearest-enclosing, B10) —
            // C# source AND the config where real services keep them (appsettings*.json,
            // web.config/app.config). See IsConnStringSource.
            var dbUses = new List<DbUse>();
            foreach (var cs in files.Where(x => IsConnStringSource(x) &&
                         string.Equals(OwningProject(x.RelPath), f.RelPath, StringComparison.OrdinalIgnoreCase)))
            {
                string text;
                try { text = File.ReadAllText(cs.AbsPath); }
                catch (IOException) { continue; }

                var lines = text.Split('\n');
                foreach (var hit in SourceTextScanner.FindConnectionStringLiterals(text))
                {
                    var ln = hit.LineNumber - 1;
                    var line = ln >= 0 && ln < lines.Length ? lines[ln] : "";
                    // Skip Web.config XDT transform directives: their connection strings are
                    // build-time placeholders (e.g. a Data Source token), not real databases.
                    if (line.Contains("xdt:", StringComparison.OrdinalIgnoreCase)) { continue; }
                    // Skip comment lines: a connection-string-shaped example inside a code or
                    // XML comment documents the format, it is not a real database.
                    if (IsCommentLine(line)) { continue; }
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

    /// <summary>Files ArchDiagram reads for connection strings: C# source, plus the config
    /// where real services actually keep them — appsettings*.json (modern builds) and
    /// *.config (web.config/app.config, older builds). All are scanned as text by the same
    /// literal detector, so a "Server=…;Database=…" string is found wherever it lives.</summary>
    /// <summary>True for lines that are code/XML comments — where connection-string-shaped
    /// text is documentation, not a live database. Covers C#/JS (<c>//</c>, <c>/*</c>,
    /// <c>*</c>), XML/config (<c>&lt;!--</c>), and script (<c>#</c>) comment leaders.</summary>
    private static bool IsCommentLine(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("//", StringComparison.Ordinal)
            || t.StartsWith("*", StringComparison.Ordinal)
            || t.StartsWith("/*", StringComparison.Ordinal)
            || t.StartsWith("<!--", StringComparison.Ordinal)
            || t.StartsWith("#", StringComparison.Ordinal);
    }

    private static bool IsConnStringSource(FileEntry f)
    {
        // Test code and fixtures are where fake/example connection strings live
        // ("Server=db1;Database=orders", sample appsettings, transform templates) — never a
        // real database the project connects to. Exclude them so scanning a repo against
        // itself doesn't invent databases from its own tests. Matches the 🧪 test filter used
        // elsewhere (see ModuleGrouper).
        if (Analysis.TestDetection.IsTest(f.RelPath)) { return false; }

        if (f.Extension == ".cs") { return true; }
        var name = Path.GetFileName(f.RelPath);
        if (f.Extension == ".json")
        {
            return name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase);
        }
        if (f.Extension == ".config")
        {
            // Skip Web.config / App.config XDT *transforms* (Web.Release.config,
            // App.Debug.config, …): they hold placeholder connection strings for build-time
            // substitution, not real databases. A runtime config's stem has no extra dot
            // ("Web" / "App"); a transform's does ("Web.Release").
            return !Path.GetFileNameWithoutExtension(name).Contains('.');
        }
        return false;
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
    private static readonly string[] ServerKeys = ["server", "data source", "host", "hostname", "address", "addr", "network address"];
    private static readonly string[] CatalogKeys = ["database", "initial catalog"];

    /// <summary>Identity for cross-service DB matching: a canonical <c>server + catalog</c>
    /// key, so the SAME physical database matches across services even when the rest of the
    /// connection string differs (key aliases like Server vs Data Source, extra options such
    /// as Encrypt/MARS, protocol/port noise). Falls back to the full normalized pair set only
    /// when neither server nor catalog can be extracted, so unparseable strings stay distinct.</summary>
    public static string NormalizeAndHash(string connectionString)
    {
        var server = CanonicalHost(ExtractValue(connectionString, ServerKeys));
        var catalog = (ExtractValue(connectionString, CatalogKeys) ?? "").Trim().ToLowerInvariant();

        string basis;
        if (server.Length > 0 || catalog.Length > 0)
        {
            basis = $"srv={server};cat={catalog}";
        }
        else
        {
            var pairs = SplitPairs(connectionString)
                .Where(p => !SecretKeys.Contains(p.Key.ToLowerInvariant()))
                .Select(p => ($"{p.Key.ToLowerInvariant()}={p.Value.ToLowerInvariant()}"))
                .OrderBy(p => p, StringComparer.Ordinal);
            basis = string.Join(';', pairs);
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(basis));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Canonicalize a SQL host so trivial differences don't split one database into
    /// two: lowercase, strip a protocol prefix (<c>tcp:</c>, <c>np:</c>), and drop a trailing
    /// <c>,port</c>. Conservative — it does not touch instance names or domain suffixes.</summary>
    private static string CanonicalHost(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) { return ""; }
        var s = server.Trim().ToLowerInvariant();
        var colon = s.IndexOf(':');           // "tcp:host" / "np:host" -> "host"
        if (colon is >= 0 and <= 4) { s = s[(colon + 1)..]; }
        var comma = s.IndexOf(',');           // "host,1433" -> "host"
        if (comma >= 0) { s = s[..comma]; }
        return s.Trim();
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
