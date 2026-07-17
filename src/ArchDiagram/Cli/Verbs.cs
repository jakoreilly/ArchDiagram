using ArchDiagram.Analysis;
using ArchDiagram.Diff;
using ArchDiagram.Graph;
using ArchDiagram.Landscape;
using ArchDiagram.Rendering;
using ArchDiagram.Site;

namespace ArchDiagram.Cli;

/// <summary>The four things <c>archdiagram</c> can do, one method each — extracted from
/// Program.cs's top-level statements (see plan.md Phase 4b), which had synthesized a single
/// "&lt;main&gt;" member summing cognitive complexity across all four verbs at once. Each
/// method here is independently scored by the self-scan instead.</summary>
internal static class Verbs
{
    /// <summary>Usage: archdiagram --landscape &lt;parent-dir&gt; [--out &lt;dir&gt;] [--no-open]
    /// Cross-references every site-*/model.json under &lt;parent-dir&gt; into a parent viewer.</summary>
    internal static int RunLandscape(string[] args)
    {
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
        var sites = SiteDiscovery.Discover(parent, lOut, diags, onlySet);
        Console.Error.WriteLine($"archdiagram: landscape found {sites.Count} site(s) under {parent}");
        var landscape = LandscapeModelBuilder.Build(sites) with { Diagnostics = diags };
        var lIndex = LandscapeGenerator.Generate(landscape, lOut, 60, DateTime.Now.ToString("yyyy-MM-dd"), title);
        Console.Error.WriteLine($"archdiagram: landscape written to {lOut}");

        if (lOpen)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(lIndex) { UseShellExecute = true }); }
            catch (Exception ex) { Console.Error.WriteLine($"archdiagram: could not auto-open the landscape: {ex.Message}"); }
        }
        return 0;
    }

    /// <summary>Usage: archdiagram --diff &lt;old model.json&gt; &lt;new model.json&gt; [--out &lt;dir&gt;] [--no-open]</summary>
    internal static int RunDiff(string[] args)
    {
        if (args.Length < 3 || args[1].StartsWith("--") || args[2].StartsWith("--"))
        {
            Console.Error.WriteLine("error: --diff requires two model.json paths (old, then new).");
            return 2;
        }
        var oldPath = Path.GetFullPath(args[1]);
        var newPath = Path.GetFullPath(args[2]);
        if (!File.Exists(oldPath)) { Console.Error.WriteLine($"error: '{oldPath}' not found."); return 2; }
        if (!File.Exists(newPath)) { Console.Error.WriteLine($"error: '{newPath}' not found."); return 2; }

        string? dOut = null;
        var dOpen = true;
        for (var i = 3; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) { dOut = args[++i]; }
            else if (args[i] == "--no-open") { dOpen = false; }
        }

        ProjectModel oldModel, newModel;
        try
        {
            oldModel = ModelJsonReader.Read(oldPath);
            newModel = ModelJsonReader.Read(newPath);
        }
        catch (Exception ex) { Console.Error.WriteLine($"error: could not read model: {ex.Message}"); return 1; }

        dOut = Path.GetFullPath(dOut ?? $"diff-{oldModel.RootName}-{newModel.RootName}", Directory.GetCurrentDirectory());
        var diffResult = ModelDiff.Compute(oldModel, newModel);
        var dIndex = DiffReport.Write(diffResult, dOut, DateTime.Now.ToString("yyyy-MM-dd"));
        Console.Error.WriteLine($"archdiagram: diff written to {dOut} "
            + $"({diffResult.AddedFiles.Count} added, {diffResult.RemovedFiles.Count} removed, {diffResult.ChangedFiles.Count} changed)");

        if (dOpen)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dIndex) { UseShellExecute = true }); }
            catch (Exception ex) { Console.Error.WriteLine($"archdiagram: could not auto-open the diff: {ex.Message}"); }
        }
        return 0;
    }

    /// <summary>Usage: archdiagram --from-model &lt;model.json&gt; [--out &lt;dir&gt;] [--no-open] [--no-wiki]
    /// Rebuilds the entire site from a saved model.json — no source scan. model.json is a
    /// complete, compact archive of everything the site renders from.</summary>
    internal static int RunFromModel(string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith("--"))
        {
            Console.Error.WriteLine("error: --from-model requires a path to a model.json.");
            return 2;
        }
        var modelPath = Path.GetFullPath(args[1]);
        if (!File.Exists(modelPath)) { Console.Error.WriteLine($"error: '{modelPath}' not found."); return 2; }

        string? fmOut = null;
        var fmOpen = true;
        var fmWiki = true;
        var fmMax = 60;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) { fmOut = args[++i]; }
            else if (args[i] == "--no-open") { fmOpen = false; }
            else if (args[i] == "--no-wiki") { fmWiki = false; }
            else if (args[i] == "--max-nodes" && i + 1 < args.Length && int.TryParse(args[i + 1], out var mn)) { fmMax = Math.Max(10, mn); i++; }
        }

        ProjectModel fmModel;
        try { fmModel = ModelJsonReader.Read(modelPath); }
        catch (Exception ex) { Console.Error.WriteLine($"error: could not read model: {ex.Message}"); return 1; }

        fmOut = Path.GetFullPath(fmOut ?? $"site-{fmModel.RootName}", Directory.GetCurrentDirectory());
        var fmOn = DateTime.Now.ToString("yyyy-MM-dd");
        // Source snippets are omitted: the model carries no source text, so a rebuilt-from-archive
        // site is faithful in every other respect. showComplexity is data-driven and stays on.
        var fmIndex = SiteGenerator.Generate(fmModel, fmOut, fmMax, fmOn, showComplexity: true, showSnippets: false, wiki: fmWiki);
        Console.Error.WriteLine($"archdiagram: rebuilt site from {modelPath} → {fmOut}");
        if (fmOpen)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fmIndex) { UseShellExecute = true }); }
            catch (Exception ex) { Console.Error.WriteLine($"archdiagram: could not auto-open: {ex.Message}"); }
        }
        return 0;
    }

    /// <summary>The default path: scan a source folder and generate its site.</summary>
    internal static int RunDefault(string[] args)
    {
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

        if (options.SarifPath is not null)
        {
            var sarifFullPath = Path.GetFullPath(options.SarifPath, Environment.CurrentDirectory);
            SarifWriter.Write(model, sarifFullPath);
            Console.Error.WriteLine($"archdiagram: SARIF log written to {sarifFullPath}");
        }

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

        // CI gate: the site is written either way; a tripped gate only affects the exit code
        // (3, distinct from usage-error 2 and crash 1 — see CliOptions.FailOn).
        if (options.FailOn.Count > 0)
        {
            var card = ScorecardBuilder.Build(model);
            var failedGates = CiGate.Evaluate(options.FailOn, card);
            if (failedGates.Count > 0)
            {
                foreach (var f in failedGates) { Console.Error.WriteLine($"archdiagram: gate failed — {f}"); }
                return 3;
            }
            Console.Error.WriteLine($"archdiagram: all {options.FailOn.Count} gate(s) passed.");
        }

        return 0;
    }
}
