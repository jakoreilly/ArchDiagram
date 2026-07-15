using System.Text;
using ArchDiagram.Landscape.Pages;
using ArchDiagram.Site;

namespace ArchDiagram.Landscape;

/// <summary>Writes the landscape (parent) site: shared assets, an overview graph,
/// a shared-databases matrix, and an interconnections page. Everything is relative-
/// path only so it opens from file://. Site nodes link into each child site.</summary>
public static class LandscapeGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static readonly (string Href, string Title, string Icon)[] Nav =
    [
        ("index.html", "Overview", "◈"),
        ("databases.html", "Shared Databases", "🗄"),
        ("interconnections.html", "Interconnections", "⇄"),
    ];

    public static string Generate(LandscapeModel model, string outDir, int maxNodes, string generatedOn, string? displayName = null)
    {
        Directory.CreateDirectory(outDir);
        SiteAssets.CopyTo(outDir);
        // Empty search index so the shared template's Ctrl+K palette loads without error on file://.
        File.WriteAllText(Path.Combine(outDir, "assets", "search-index.js"), "window.ARCH_SEARCH_INDEX = [];\n", Utf8NoBom);

        var brandSub = string.IsNullOrWhiteSpace(displayName) ? "Landscape" : displayName;
        Write(outDir, "index.html", "Overview", "index.html",
            PageTemplate.Crumbs((null, "Landscape")), LandscapeIndexPage.Body(model, maxNodes, generatedOn), brandSub);
        Write(outDir, "databases.html", "Shared Databases", "databases.html",
            PageTemplate.Crumbs(("index.html", "Landscape"), (null, "Shared Databases")), LandscapeDatabasesPage.Body(model), brandSub);
        Write(outDir, "interconnections.html", "Interconnections", "interconnections.html",
            PageTemplate.Crumbs(("index.html", "Landscape"), (null, "Interconnections")), LandscapeInterconnectionsPage.Body(model), brandSub);

        return Path.Combine(outDir, "index.html");
    }

    private static void Write(string outDir, string fileName, string title, string activeHref, string crumbs, string body, string brandSub)
    {
        var html = PageTemplate.Render(title, brandSub, activeHref, "", crumbs, body, Nav);
        File.WriteAllText(Path.Combine(outDir, fileName), html, Utf8NoBom);
    }
}
