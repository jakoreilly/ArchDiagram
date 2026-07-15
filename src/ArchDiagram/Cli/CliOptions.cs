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

    public static CliOptions? Parse(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine("Usage: archdiagram <path-to-project> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]... [--no-complexity] [--no-snippets] [--no-wiki] [--source-link-type <github|gitlab|local>] [--source-link-base <url>] [--source-link-ref <branch>] [--descriptions <path>]");
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
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) { outDir = args[++i]; }
            else if (args[i] == "--no-open") { open = false; }
            else if (args[i] == "--max-nodes" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n)) { maxNodes = Math.Max(10, n); i++; }
            else if (args[i] == "--exclude" && i + 1 < args.Length) { exclude.Add(args[++i]); }
            else if (args[i] == "--no-complexity") { showComplexity = false; }
            else if (args[i] == "--no-snippets") { showSnippets = false; }
            else if (args[i] == "--no-wiki") { wiki = false; }
            else if (args[i] == "--source-link-type" && i + 1 < args.Length) { slType = args[++i]; }
            else if (args[i] == "--source-link-base" && i + 1 < args.Length) { slBase = args[++i]; }
            else if (args[i] == "--source-link-ref" && i + 1 < args.Length) { slRef = args[++i]; }
            else if (args[i] == "--descriptions" && i + 1 < args.Length) { descriptionsPath = args[++i]; }
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
        };
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
