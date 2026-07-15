using ArchDiagram.Graph;
using ArchDiagram.Site.Pages;

namespace ArchDiagram.Tests;

public class IndexPageTests
{
    private static ProjectModel SingleFolderModel() => new()
    {
        RootName = "Tiny",
        SourcePath = "C:/tiny",
        Files =
        {
            new FileNode { RelPath = "src/A.cs", Slug = "src_A_cs", Language = "C#", Loc = 10 },
            new FileNode { RelPath = "src/B.cs", Slug = "src_B_cs", Language = "C#", Loc = 20 },
        },
        FileDependencies = { new DepEdge { FromSlug = "src_A_cs", ToSlug = "src_B_cs" } },
        LanguageLoc = { ["C#"] = 30 },
    };

    [Fact]
    public void Trivial_architecture_leads_with_graph_and_collapses_static_diagram()
    {
        var html = IndexPage.Body(SingleFolderModel(), maxNodes: 60, generatedOn: "2026-01-01");
        Assert.Contains("id=\"graph3d-root\"", html);
        Assert.Contains("data-compact=\"1\"", html);          // compact embed
        Assert.Contains("Static architecture diagram", html);  // Mermaid tucked into details
    }
}
