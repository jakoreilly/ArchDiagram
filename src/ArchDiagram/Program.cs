// ArchDiagram — point it at any project folder, get a fully-offline static HTML
// documentation site: architecture overview, folder structure, file dependency
// graphs, C# types + heuristic call graphs, and a drill-down page per file.
// All diagrams pan/zoom and export to PNG. Nothing is fetched at runtime.
//
// Usage: archdiagram <path-to-project> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]...
using ArchDiagram;
using ArchDiagram.Cli;
using ArchDiagram.Site;

if (args.Length > 0 && args[0] == "--landscape")
{
    // Usage: archdiagram --landscape <parent-dir> [--out <dir>] [--no-open]
    // Cross-references every site-*/model.json under <parent-dir> into a parent viewer.
    var parent = args.Length > 1 && !args[1].StartsWith("--") ? Path.GetFullPath(args[1]) : Directory.GetCurrentDirectory();
    string? lOut = null, only = null, title = null;
    var lOpen = true;
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--out" && i + 1 < args.Length) { lOut = args[++i]; }
        else if (args[i] == "--no-open") { lOpen = false; }
        else if (args[i] == "--only" && i + 1 < args.Length) { only = args[++i]; }
        else if (args[i] == "--title" && i + 1 < args.Length) { title = args[++i]; }
    }
    lOut = Path.GetFullPath(lOut ?? Path.Combine(parent, "site-landscape"), Directory.GetCurrentDirectory());

    var diags = new List<string>();
    ISet<string>? onlySet = only is null ? null
        : new HashSet<string>(only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
    var sites = ArchDiagram.Landscape.SiteDiscovery.Discover(parent, lOut, diags, onlySet);
    Console.Error.WriteLine($"archdiagram: landscape found {sites.Count} site(s) under {parent}");
    var landscape = ArchDiagram.Landscape.LandscapeModelBuilder.Build(sites) with { Diagnostics = diags };
    var lIndex = ArchDiagram.Landscape.LandscapeGenerator.Generate(landscape, lOut, 60, DateTime.Now.ToString("yyyy-MM-dd"), title);
    Console.Error.WriteLine($"archdiagram: landscape written to {lOut}");

    if (lOpen)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lIndex) { UseShellExecute = true }); }
        catch (Exception ex) { Console.Error.WriteLine($"archdiagram: could not auto-open the landscape: {ex.Message}"); }
    }
    return 0;
}

var options = CliOptions.Parse(args, out var exitCode);
if (options is null) { return exitCode; }

Console.Error.WriteLine($"archdiagram: scanning {options.SourcePath}");
var model = Pipeline.BuildModel(options);
Console.Error.WriteLine($"archdiagram: {model.Files.Count} files, {model.Projects.Count} projects, " +
    $"{model.FileDependencies.Count} file links, {model.Calls.Count} call links, {model.Diagnostics.Count} diagnostics");

var generatedOn = DateTime.Now.ToString("yyyy-MM-dd");
var indexPath = SiteGenerator.Generate(model, options.OutDir, options.MaxNodes, generatedOn,
    options.ShowComplexity, options.ShowSnippets, options.Wiki);
Console.Error.WriteLine($"archdiagram: site written to {options.OutDir}");

if (options.Open)
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(indexPath) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"archdiagram: could not auto-open the site: {ex.Message}");
    }
}

return 0;
