using System.Security.Cryptography;
using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Cli;
using ArchDiagram.Graph;
using ArchDiagram.Scanning;

namespace ArchDiagram;

public static class Pipeline
{
    private const long MaxAnalyzeBytes = 1024 * 1024; // skip deep analysis beyond 1 MB
    private const long EstimatedBytesPerLine = 40;     // rough LOC estimate for size-skipped files

    /// <summary>Lines in a text: newline count, plus one for a final unterminated line.
    /// A file ending in "\n" has exactly newline-count lines (no phantom trailing line).</summary>
    internal static int CountLines(string content)
    {
        if (content.Length == 0) { return 0; }
        var newlines = content.AsSpan().Count('\n');
        return content[^1] == '\n' ? newlines : newlines + 1;
    }

    /// <summary>Read-only, thread-safe analyzer set shared across every parallel
    /// <see cref="AnalyzeOne"/> call — none of them mutate instance state.</summary>
    private sealed record Analyzers(
        CSharpSyntaxAnalyzer CSharp, CSharpUsingAnalyzer CSharpFallback,
        List<ILanguageAnalyzer> Regex, KnownFileAnalyzer Known);

    /// <summary>Everything <see cref="AnalyzeOne"/> learns about a single file. No shared
    /// mutable state — safe to compute on any thread. The caller assigns the slug and merges
    /// diagnostics/LOC serially afterward, since slug de-duplication and diagnostic order both
    /// depend on processing entries in their original (sorted) order.</summary>
    private readonly record struct FileResult(FileNode NodeNoSlug, string Language, int Loc, List<string> Diagnostics);

    public static ProjectModel BuildModel(CliOptions options)
    {
        var diagnostics = new List<string>();
        var entries = FileSystemScanner.Scan(options.SourcePath, options.Exclude, diagnostics);
        var authored = DescriptionsLoader.Load(options.DescriptionsPath, options.SourcePath, diagnostics);
        // Whole-repo git aggregation, once, before the parallel scan — a read-only lookup each
        // AnalyzeOne consults by RelPath. Non-fatal: a non-git tree yields an empty result and
        // the churn fields stay at their defaults (see GitHistory).
        var git = GitHistory.Analyze(options.SourcePath, diagnostics);

        var analyzers = new Analyzers(
            new CSharpSyntaxAnalyzer(), new CSharpUsingAnalyzer(),
            [
                new TsJsImportAnalyzer(), new PythonImportAnalyzer(), new PowerShellImportAnalyzer(),
                new GoImportAnalyzer(), new JavaImportAnalyzer(), new RustImportAnalyzer(),
                new RubyImportAnalyzer(), new PhpImportAnalyzer(), new CppImportAnalyzer(),
            ],
            new KnownFileAnalyzer());

        // Parallel map: each file is read + analysed independently (CPU-bound Roslyn/regex
        // work dominates at scale). No shared mutable state is touched here.
        var results = new FileResult[entries.Count];
        Parallel.For(0, entries.Count, idx => { results[idx] = AnalyzeOne(entries[idx], analyzers, authored, git.Files); });

        // Serial reduce — order-dependent shared state (Constraint 1: determinism).
        // Slug de-duplication and diagnostic order both depend on processing entries in their
        // original (sorted) order, so this pass must not run in parallel.
        var files = new List<FileNode>(entries.Count);
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var languageLoc = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in results)
        {
            diagnostics.AddRange(r.Diagnostics);
            var node = r.NodeNoSlug with { Slug = MakeSlug(r.NodeNoSlug.RelPath, usedSlugs) };
            files.Add(node);
            if (r.Loc > 0) { languageLoc[r.Language] = languageLoc.GetValueOrDefault(r.Language) + r.Loc; }
        }

        var projects = CsprojScanner.Scan(options.SourcePath, entries, diagnostics);
        var databases = CsprojScanner.BuildDbNodes(projects);
        var deps = ImportResolver.Resolve(files);
        var calls = CallGraphBuilder.Build(files);

        var rootName = Path.GetFileName(options.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName)) { rootName = "project"; }

        return new ProjectModel
        {
            RootName = rootName,
            SourcePath = options.SourcePath,
            Files = files,
            Projects = projects,
            Databases = databases,
            FileDependencies = deps,
            Calls = calls,
            Diagnostics = diagnostics,
            LanguageLoc = languageLoc,
            Description = authored.Project,
            FolderDescriptions = new Dictionary<string, string>(authored.Folders, StringComparer.OrdinalIgnoreCase),
            Layers = LayersLoader.Load(options.SourcePath, diagnostics),
            Git = git.Info.Available ? git.Info : null,
            SourceLink = options.SourceLinkType is "none" or ""
                ? null
                : new SourceLink { Type = options.SourceLinkType, Base = options.SourceLinkBase, Ref = options.SourceLinkRef },
        };
    }

    /// <summary>Pure per-file analysis: read + parse one file and produce everything a
    /// <see cref="FileNode"/> needs except its slug (assigned serially, since slug
    /// de-duplication depends on processing order). Touches no shared mutable state, so it
    /// is safe to call from any thread; this is also the exact seam a future incremental
    /// cache would memoise on <c>(entry.AbsPath, mtime, size)</c> — do not inline it back
    /// into the loop.</summary>
    private static FileResult AnalyzeOne(FileEntry entry, Analyzers a, AuthoredDescriptions authored,
        IReadOnlyDictionary<string, GitHistory.FileChurn> churn)
    {
        var localDiagnostics = new List<string>();
        var language = KnownFileAnalyzer.LanguageByExtension.GetValueOrDefault(entry.Extension, "Other");

        string content = "";
        var loc = 0;
        var analyzable = language != "Other" && entry.SizeBytes <= MaxAnalyzeBytes;
        if (analyzable)
        {
            try
            {
                content = File.ReadAllText(entry.AbsPath);
                loc = CountLines(content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                localDiagnostics.Add($"Could not read {entry.RelPath}: {ex.Message}");
                analyzable = false;
            }
        }
        else if (language != "Other")
        {
            // Too big to parse, but still a known-language source: estimate LOC from
            // size (deterministic) so it isn't dropped from totals and "largest files".
            loc = (int)(entry.SizeBytes / EstimatedBytesPerLine);
            localDiagnostics.Add($"Skipped deep analysis of {entry.RelPath} ({StructurePageBytes(entry.SizeBytes)} exceeds 1 MB limit); LOC estimated from size.");
        }

        FileFacts facts = new() { Language = language };
        if (analyzable && content.Length > 0)
        {
            try
            {
                if (a.CSharp.CanHandle(entry.Extension)) { facts = a.CSharp.Analyze(entry, content); }
                else
                {
                    var analyzer = a.Regex.FirstOrDefault(x => x.CanHandle(entry.Extension)) ?? a.Known;
                    facts = analyzer.Analyze(entry, content);
                }
            }
            catch (Exception ex)
            {
                localDiagnostics.Add($"Analysis failed for {entry.RelPath}: {ex.Message}");
                if (a.CSharp.CanHandle(entry.Extension))
                {
                    try { facts = a.CSharpFallback.Analyze(entry, content); } catch { facts = new FileFacts { Language = language }; }
                }
            }
        }

        var c = churn.GetValueOrDefault(entry.RelPath);
        var node = new FileNode
        {
            RelPath = entry.RelPath,
            Slug = "", // assigned serially in BuildModel's reduce pass
            Language = facts.Language,
            SizeBytes = entry.SizeBytes,
            Loc = loc,
            IsTest = TestDetection.IsTest(entry.RelPath),
            IsVendored = VendoredDetection.IsVendored(entry.RelPath),
            Imports = facts.Imports,
            Types = facts.Types,
            Todos = analyzable && content.Length > 0 ? TodoScanner.Scan(content, facts.Language) : [],
            CommitCount = c?.CommitCount ?? 0,
            AuthorCount = c?.AuthorCount ?? 0,
            PrincipalAuthor = c?.PrincipalAuthor ?? "",
            LastModified = c?.LastModified ?? "",
        };
        PurposeHeuristics.Apply(node, content);
        // Authored file description (from the sidecar) wins over the heuristic purpose.
        if (authored.Files.TryGetValue(entry.RelPath, out var authoredPurpose))
        {
            node.Purpose = authoredPurpose;
            node.PurposeSource = "authored";
        }

        return new FileResult(node, facts.Language, loc, localDiagnostics);
    }

    /// <summary>Slug: relative path with non-alphanumerics -> '_', capped at 100 chars
    /// with an 8-char hash suffix on overflow or collision (Windows MAX_PATH safety).</summary>
    public static string MakeSlug(string relPath, HashSet<string> used)
    {
        var sb = new StringBuilder(relPath.Length);
        foreach (var ch in relPath)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        var slug = sb.ToString();
        if (slug.Length > 100 || !used.Add(slug))
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(relPath)))[..8];
            slug = (slug.Length > 91 ? slug[..91] : slug) + "_" + hash;
            var candidate = slug;
            var i = 2;
            while (!used.Add(candidate)) { candidate = slug + "_" + i++; }
            slug = candidate;
        }
        return slug;
    }

    private static string StructurePageBytes(long bytes) => $"{bytes / (1024.0 * 1024.0):F1} MB";
}
