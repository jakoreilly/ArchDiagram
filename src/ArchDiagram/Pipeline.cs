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
        var newlines = content.Count(c => c == '\n');
        return content[^1] == '\n' ? newlines : newlines + 1;
    }

    public static ProjectModel BuildModel(CliOptions options)
    {
        var diagnostics = new List<string>();
        var entries = FileSystemScanner.Scan(options.SourcePath, options.Exclude, diagnostics);
        var authored = DescriptionsLoader.Load(options.DescriptionsPath, options.SourcePath, diagnostics);

        var csharp = new CSharpSyntaxAnalyzer();
        var csharpFallback = new CSharpUsingAnalyzer();
        var regexAnalyzers = new List<ILanguageAnalyzer> { new TsJsImportAnalyzer(), new PythonImportAnalyzer(), new PowerShellImportAnalyzer() };
        var known = new KnownFileAnalyzer();

        var files = new List<FileNode>();
        var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var languageLoc = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
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
                    diagnostics.Add($"Could not read {entry.RelPath}: {ex.Message}");
                    analyzable = false;
                }
            }
            else if (language != "Other")
            {
                // Too big to parse, but still a known-language source: estimate LOC from
                // size (deterministic) so it isn't dropped from totals and "largest files".
                loc = (int)(entry.SizeBytes / EstimatedBytesPerLine);
                diagnostics.Add($"Skipped deep analysis of {entry.RelPath} ({StructurePageBytes(entry.SizeBytes)} exceeds 1 MB limit); LOC estimated from size.");
            }

            FileFacts facts = new() { Language = language };
            if (analyzable && content.Length > 0)
            {
                try
                {
                    if (csharp.CanHandle(entry.Extension)) { facts = csharp.Analyze(entry, content); }
                    else
                    {
                        var analyzer = regexAnalyzers.FirstOrDefault(a => a.CanHandle(entry.Extension)) ?? known;
                        facts = analyzer.Analyze(entry, content);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Analysis failed for {entry.RelPath}: {ex.Message}");
                    if (csharp.CanHandle(entry.Extension))
                    {
                        try { facts = csharpFallback.Analyze(entry, content); } catch { facts = new FileFacts { Language = language }; }
                    }
                }
            }

            var node = new FileNode
            {
                RelPath = entry.RelPath,
                Slug = MakeSlug(entry.RelPath, usedSlugs),
                Language = facts.Language,
                SizeBytes = entry.SizeBytes,
                Loc = loc,
                IsTest = TestDetection.IsTest(entry.RelPath),
                IsVendored = VendoredDetection.IsVendored(entry.RelPath),
                Imports = facts.Imports,
                Types = facts.Types,
                Todos = analyzable && content.Length > 0 ? TodoScanner.Scan(content, facts.Language) : [],
            };
            PurposeHeuristics.Apply(node, content);
            // Authored file description (from the sidecar) wins over the heuristic purpose.
            if (authored.Files.TryGetValue(entry.RelPath, out var authoredPurpose))
            {
                node.Purpose = authoredPurpose;
                node.PurposeSource = "authored";
            }
            files.Add(node);

            if (loc > 0) { languageLoc[facts.Language] = languageLoc.GetValueOrDefault(facts.Language) + loc; }
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
            SourceLink = options.SourceLinkType is "none" or ""
                ? null
                : new SourceLink { Type = options.SourceLinkType, Base = options.SourceLinkBase, Ref = options.SourceLinkRef },
        };
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
