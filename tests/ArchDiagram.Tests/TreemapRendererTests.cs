using System.Text.RegularExpressions;
using System.Xml.Linq;
using ArchDiagram.Graph;
using ArchDiagram.Site;

namespace ArchDiagram.Tests;

public class TreemapRendererTests
{
    private static List<FileNode> Files() =>
    [
        new() { RelPath = "src/Big.cs", Slug = "src_big_cs", Language = "C#", Loc = 400 },
        new() { RelPath = "src/Small.cs", Slug = "src_small_cs", Language = "C#", Loc = 20 },
        new() { RelPath = "web/app.ts", Slug = "web_app_ts", Language = "TypeScript/JavaScript", Loc = 120 },
        new() { RelPath = "docs/readme.md", Slug = "docs_readme_md", Language = "Markdown", Loc = 0 }, // no LOC → excluded
    ];

    [Fact]
    public void Emits_one_rect_per_file_with_loc()
    {
        var svg = TreemapRenderer.Render(Files());
        Assert.Equal(3, Regex.Matches(svg, "<rect ").Count); // the 0-LOC file is excluded
    }

    [Fact]
    public void Is_valid_xml()
    {
        var svg = TreemapRenderer.Render(Files());
        var doc = XDocument.Parse(svg); // throws if malformed / unbalanced / unescaped
        Assert.Equal("svg", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void Has_no_http_urls()
    {
        Assert.DoesNotContain("http", TreemapRenderer.Render(Files()));
    }

    [Fact]
    public void All_hrefs_point_at_file_pages()
    {
        var svg = TreemapRenderer.Render(Files());
        foreach (Match m in Regex.Matches(svg, "href=\"([^\"]+)\""))
        {
            Assert.Matches(new Regex(@"^files/[^""]+\.html$"), m.Groups[1].Value);
        }
    }

    [Fact]
    public void Is_deterministic()
    {
        Assert.Equal(TreemapRenderer.Render(Files()), TreemapRenderer.Render(Files()));
    }

    [Fact]
    public void Excludes_test_files()
    {
        var files = new List<FileNode>
        {
            new() { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100 },
            new() { RelPath = "tests/ATests.cs", Slug = "at", Language = "C#", Loc = 100, IsTest = true },
        };
        var svg = TreemapRenderer.Render(files);
        Assert.Contains("files/a.html", svg);
        Assert.DoesNotContain("files/at.html", svg);
    }

    [Fact]
    public void Empty_when_no_loc()
    {
        Assert.Equal("", TreemapRenderer.Render([new FileNode { RelPath = "x.md", Slug = "x", Language = "Markdown", Loc = 0 }]));
    }
}
