namespace ArchDiagram.Cli;

public sealed record CliOptions
{
    public required string SourcePath { get; init; }
    public string OutDir { get; init; } = "site";
    public bool Open { get; init; } = true;
    public int MaxNodes { get; init; } = 60;
    public List<string> Exclude { get; init; } = [];
    /// <summary>Show complexity reference points (rankings + badges). Disable with --no-complexity.</summary>
    public bool ShowComplexity { get; init; } = true;
    /// <summary>Show inline source snippets for the most complex methods. Disable with --no-snippets.</summary>
    public bool ShowSnippets { get; init; } = true;
    /// <summary>Also write an offline wiki export (Confluence Storage Format). Disable with --no-wiki.</summary>
    public bool Wiki { get; init; } = true;
    /// <summary>Source-link host: "github" | "gitlab" | "local" | "none".</summary>
    public string SourceLinkType { get; init; } = "none";
    /// <summary>Repo/web base or local root used to build source links.</summary>
    public string SourceLinkBase { get; init; } = "";
    /// <summary>Branch/tag/commit for web source links (ignored for local).</summary>
    public string SourceLinkRef { get; init; } = "main";
    /// <summary>Optional path to an authored descriptions sidecar; null probes the source root.</summary>
    public string? DescriptionsPath { get; init; }
    /// <summary>CI gates to check after generation: "cycles" | "layering" | "secrets" |
    /// "drift" | "scorecard" (comma-separated). A tripped gate exits 3 (distinct from usage
    /// error 2 and crash 1) — the site is still written. Empty = no CI gating.</summary>
    public List<string> FailOn { get; init; } = [];
    /// <summary>Optional path to write a SARIF 2.1.0 log of the refactoring backlog + any
    /// failed scorecard signal (for code-scanning dashboards); null = don't write one.</summary>
    public string? SarifPath { get; init; }

    public static CliOptions? Parse(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine("Usage: archdiagram <path-to-project> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]... [--no-complexity] [--no-snippets] [--no-wiki] [--source-link-type <github|gitlab|local>] [--source-link-base <url>] [--source-link-ref <branch>] [--descriptions <path>] [--fail-on <gate>[,<gate>...]] [--sarif <path>]");
            Console.Error.WriteLine($"  --fail-on gates: {string.Join(", ", Analysis.CiGate.KnownGates.Keys.OrderBy(k => k, StringComparer.Ordinal))}. On a tripped gate the site is still written and the process exits 3 (2 = usage error, 1 = crash).");
            exitCode = args.Length == 0 ? 2 : 0;
            return null;
        }

        var source = Path.GetFullPath(args[0]);
        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"error: '{source}' is not a directory.");
            exitCode = 2;
            return null;
        }

        string? outDir = null;
        var open = true;
        var maxNodes = 60;
        var exclude = new List<string>();
        var showComplexity = true;
        var showSnippets = true;
        var wiki = true;
        var slType = "none";
        var slBase = "";
        var slRef = "main";
        string? descriptionsPath = null;
        string? sarifPath = null;
        var failOn = new List<string>();

        // Flags grouped by shape (no-value boolean vs. single-value) so the loop below has
        // one branch per SHAPE, not one per flag — a long if/else-if chain (one branch per
        // flag, 14 of them) is exactly what drives cognitive complexity sky-high; the flag ->
        // handler lookup does the same work as a switch without the branch count.
        // --max-nodes and --fail-on keep their own branch: both need extra validation beyond
        // "does a value follow" (int parsing; gate-name checking), so folding them into the
        // generic dictionaries would either lose the original TryParse-failure-falls-through
        // behavior (see the GOTCHA at Verification V4 in plan.md) or need the same
        // multi-statement body a dictionary entry can't cleanly express.
        var boolFlags = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["--no-open"] = () => open = false,
            ["--no-complexity"] = () => showComplexity = false,
            ["--no-snippets"] = () => showSnippets = false,
            ["--no-wiki"] = () => wiki = false,
        };
        var valueFlags = new Dictionary<string, Action<string>>(StringComparer.Ordinal)
        {
            ["--out"] = v => outDir = v,
            ["--exclude"] = v => exclude.Add(v),
            ["--source-link-type"] = v => slType = v,
            ["--source-link-base"] = v => slBase = v,
            ["--source-link-ref"] = v => slRef = v,
            ["--descriptions"] = v => descriptionsPath = v,
            ["--sarif"] = v => sarifPath = v,
        };

        for (var i = 1; i < args.Length; i++)
        {
            if (boolFlags.TryGetValue(args[i], out var setBool)) { setBool(); }
            else if (valueFlags.TryGetValue(args[i], out var setValue) && i + 1 < args.Length) { setValue(args[++i]); }
            else if (args[i] == "--max-nodes" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n)) { maxNodes = Math.Max(10, n); i++; }
            else if (args[i] == "--fail-on" && i + 1 < args.Length)
            {
                if (!TryParseFailOn(args[++i], failOn, out exitCode)) { return null; }
            }
            else
            {
                Console.Error.WriteLine($"error: unknown argument '{args[i]}'.");
                exitCode = 2;
                return null;
            }
        }

        // When --out is omitted, derive a distinct folder from the source name
        // (e.g. "site-core-service") so sites for different projects can coexist.
        outDir ??= "site-" + Slugify(Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        return new CliOptions
        {
            SourcePath = source,
            OutDir = Path.GetFullPath(outDir, Environment.CurrentDirectory),
            Open = open,
            MaxNodes = maxNodes,
            Exclude = exclude,
            ShowComplexity = showComplexity,
            ShowSnippets = showSnippets,
            Wiki = wiki,
            SourceLinkType = slType,
            SourceLinkBase = slBase,
            SourceLinkRef = slRef,
            DescriptionsPath = descriptionsPath,
            FailOn = failOn,
            SarifPath = sarifPath,
        };
    }

    /// <summary>Parses and validates a "--fail-on a,b" argument, appending accepted gate names
    /// to <paramref name="failOn"/>. Returns false (with <paramref name="exitCode"/> set to 2
    /// and an error already printed) on an unknown gate name.</summary>
    private static bool TryParseFailOn(string arg, List<string> failOn, out int exitCode)
    {
        exitCode = 0;
        var requested = arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unknown = requested.Where(g => !Analysis.CiGate.KnownGates.ContainsKey(g)).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"error: unknown --fail-on gate(s): {string.Join(", ", unknown)}. "
                + $"Valid gates: {string.Join(", ", Analysis.CiGate.KnownGates.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
            exitCode = 2;
            return false;
        }
        failOn.AddRange(requested);
        return true;
    }

    /// <summary>Reduces a folder name to a filesystem- and URL-safe slug.</summary>
    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return "project"; }
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }
        var slug = sb.ToString().Trim('-', '.');
        return slug.Length == 0 ? "project" : slug;
    }
}
