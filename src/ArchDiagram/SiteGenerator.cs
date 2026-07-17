using System.Text;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;
using ArchDiagram.Site;
using ArchDiagram.Site.Pages;

namespace ArchDiagram;

/// <summary>Writes the complete static site: shared assets, overview + drill-down
/// pages, one page per file, and model.json. Everything is relative-path only.</summary>
public static class SiteGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Generate(ProjectModel model, string outDir, int maxNodes, string generatedOn,
        bool showComplexity = false, bool showSnippets = false, bool wiki = false)
    {
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, "files"));
        SiteAssets.CopyTo(outDir);

        // Computed once: fan-in/out, call indexes, scorecard, metrics, importance ranking and
        // the 3D graph payload. Every page below reuses this instead of recomputing its own
        // copy.
        var ctx = SiteContext.Build(model);

        WritePage(outDir, "index.html", "Overview", model, "index.html", "",
            PageTemplate.Crumbs((null, "Overview")),
            IndexPage.Body(ctx, maxNodes, generatedOn));

        WritePage(outDir, "brief.html", "System Brief", model, "brief.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "System Brief")),
            BriefPage.Body(model, generatedOn));

        WritePage(outDir, "guide.html", "Guide", model, "guide.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Guide")),
            GuidePage.Body(model));

        WritePage(outDir, "structure.html", "Structure", model, "structure.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Structure")),
            StructurePage.Body(model));

        WritePage(outDir, "dependencies.html", "Dependencies", model, "dependencies.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Dependencies")),
            DependenciesPage.Body(model, maxNodes));

        WritePage(outDir, "modules.html", "Modules", model, "modules.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Modules")),
            ModulesPage.Body(model, maxNodes));

        WritePage(outDir, "layers.html", "Dependency Direction", model, "layers.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Dependency Direction")),
            LayeringPage.Body(model));

        WritePage(outDir, "metrics.html", "Architecture Metrics", model, "metrics.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Metrics")),
            MetricsPage.Body(ctx));

        WritePage(outDir, "scorecard.html", "Architecture Scorecard", model, "scorecard.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Scorecard")),
            ScorecardPage.Body(model));

        WritePage(outDir, "refactor.html", "Refactoring Backlog", model, "refactor.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Refactoring")),
            RefactorPage.Body(model));

        WritePage(outDir, "types.html", "Types & Members", model, "types.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Types & Members")),
            TypesPage.Body(model, maxNodes));

        WritePage(outDir, "api.html", "API Surface", model, "api.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "API Surface")),
            ApiSurfacePage.Body(model));

        WritePage(outDir, "calls.html", "Call Graph", model, "calls.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Call Graph")),
            CallsPage.Body(model, maxNodes));

        WritePage(outDir, "packages.html", "External Dependencies", model, "packages.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Packages")),
            PackagesPage.Body(model));

        WritePage(outDir, "config.html", "Config & Secrets", model, "config.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Config & Secrets")),
            ConfigSecretsPage.Body(model));

        WritePage(outDir, "hotspots.html", "Hotspots & Metrics", model, "hotspots.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Hotspots")),
            HotspotsPage.Body(ctx, showComplexity));

        WritePage(outDir, "evolution.html", "Evolution", model, "evolution.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Evolution")),
            EvolutionPage.Body(model));

        WritePage(outDir, "graph.html", "Graph (3D)", model, "graph.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Graph (3D)")),
            GraphPage.Body(model, ctx.GraphJson));

        WritePage(outDir, "explore.html", "Explore", model, "explore.html", "",
            PageTemplate.Crumbs(("index.html", "Overview"), (null, "Explore")),
            ExplorePage.Body(model, ctx.GraphJson));

        foreach (var file in model.Files)
        {
            var crumbs = PageTemplate.Crumbs(("../index.html", "Overview"), ("../structure.html", "Structure"), (null, file.RelPath));
            var html = PageTemplate.Render(file.RelPath, model.RootName, "", "../", crumbs, FilePage.Body(ctx, file, maxNodes, showComplexity, showSnippets), navItems: null, sourceLink: model.SourceLink);
            File.WriteAllText(Path.Combine(outDir, "files", file.Slug + ".html"), html, Utf8NoBom);
        }

        ModelJsonWriter.Write(model, Path.Combine(outDir, "model.json"));
        GraphDataWriter.WriteJson(ctx.GraphJson, Path.Combine(outDir, "graph.json"));
        SearchIndexWriter.Write(model, Path.Combine(outDir, "assets", "search-index.js"));
        MarkdownExporter.Write(ctx, Path.Combine(outDir, "ARCHITECTURE.md"), maxNodes, generatedOn);
        if (wiki)
        {
            WikiExporter.Write(ctx, Path.Combine(outDir, "wiki"), maxNodes, generatedOn, showComplexity);
        }
        return Path.Combine(outDir, "index.html");
    }

    private static void WritePage(string outDir, string fileName, string title, ProjectModel model,
        string activeHref, string relRoot, string crumbs, string body)
    {
        var html = PageTemplate.Render(title, model.RootName, activeHref, relRoot, crumbs, body, navItems: null, sourceLink: model.SourceLink);
        File.WriteAllText(Path.Combine(outDir, fileName), html, Utf8NoBom);
    }
}
