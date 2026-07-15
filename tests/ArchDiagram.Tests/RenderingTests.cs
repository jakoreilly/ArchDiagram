using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Tests;

public class MermaidRendererTests
{
    [Fact]
    public void Escapes_generics_and_quotes_in_labels()
    {
        var d = MermaidRenderer.Render(
            [new DiagramNode("a", "List<Foo> \"x\" {y}|z", "file")], []);
        Assert.Contains("n001[\"List#lt;Foo#gt; #quot;x#quot; #123;y#125;#124;z\"]", d.Mermaid);
        Assert.DoesNotContain("List<Foo>", d.Mermaid);
    }

    [Fact]
    public void Trim_metadata_drives_the_diagram_block_banner()
    {
        var d = MermaidRenderer.Render([new DiagramNode("a", "A", "file")], [], totalNodes: 10);
        Assert.True(d.Trimmed);
        Assert.Equal(1, d.ShownNodes);
        Assert.Equal(10, d.TotalNodes);

        var html = ArchDiagram.Site.PageTemplate.DiagramBlock("x", d, "png");
        Assert.Contains("diagram-trim", html);
        Assert.Contains("Showing", html);

        // A non-trimmed diagram carries no banner.
        var full = MermaidRenderer.Render([new DiagramNode("a", "A", "file")], []);
        Assert.False(full.Trimmed);
        Assert.DoesNotContain("diagram-trim", ArchDiagram.Site.PageTemplate.DiagramBlock("y", full, "png"));
    }

    [Fact]
    public void Aliases_are_deterministic_and_sequential()
    {
        var nodes = new[] { new DiagramNode("x", "X", ""), new DiagramNode("y", "Y", "") };
        var d = MermaidRenderer.Render(nodes, [new DiagramEdge("x", "y", "calls")]);
        Assert.Contains("n001[\"X\"]", d.Mermaid);
        Assert.Contains("n002[\"Y\"]", d.Mermaid);
        Assert.Contains("n001 -->|\"calls\"| n002", d.Mermaid);
    }

    [Fact]
    public void Dashed_edges_and_tooltips_are_emitted()
    {
        var d = MermaidRenderer.Render(
            [new DiagramNode("a", "A", "", Tooltip: "hello"), new DiagramNode("b", "B", "")],
            [new DiagramEdge("a", "b", "", Dashed: true)]);
        Assert.Contains("n001 -.-> n002", d.Mermaid);
        Assert.Equal("hello", d.Tooltips["n001"]);
        Assert.False(d.Tooltips.ContainsKey("n002"));
    }

    [Fact]
    public void Edges_to_unknown_nodes_are_dropped()
    {
        var d = MermaidRenderer.Render([new DiagramNode("a", "A", "")], [new DiagramEdge("a", "missing")]);
        Assert.DoesNotContain("-->", d.Mermaid);
    }

    [Fact]
    public void Hrefs_map_only_nodes_that_set_a_href()
    {
        var d = MermaidRenderer.Render(
            [new DiagramNode("a", "A", "file", Href: "files/a.html"), new DiagramNode("b", "B", "file")],
            []);
        Assert.Equal("files/a.html", d.Hrefs["n001"]);
        Assert.False(d.Hrefs.ContainsKey("n002"));
    }

    [Fact]
    public void Href_does_not_change_the_mermaid_text()
    {
        var nodes = new[] { new DiagramNode("a", "A", "file"), new DiagramNode("b", "B", "file") };
        var withHref = new[] { nodes[0] with { Href = "files/a.html" }, nodes[1] with { Href = "files/b.html" } };
        var edges = new[] { new DiagramEdge("a", "b") };
        // The href map is out-of-band metadata; the rendered flowchart must be byte-identical.
        Assert.Equal(MermaidRenderer.Render(nodes, edges).Mermaid, MermaidRenderer.Render(withHref, edges).Mermaid);
    }
}

public class DependencyControlsTests
{
    private static ProjectModel SampleModel() => new()
    {
        RootName = "Sample",
        SourcePath = "C:/sample",
        Files =
        {
            new FileNode { RelPath = "src/Program.cs", Slug = "src_Program_cs", Language = "C#" },
            new FileNode { RelPath = "src/Helper.cs", Slug = "src_Helper_cs", Language = "C#" },
        },
        FileDependencies =
        {
            // one internal edge and one external-package edge
            new DepEdge { FromSlug = "src_Program_cs", ToSlug = "src_Helper_cs" },
            new DepEdge { FromSlug = "src_Program_cs", ExternalTarget = "System.Data" },
        },
    };

    [Fact]
    public void Dependencies_page_emits_layer_toggles_and_highlight_filter()
    {
        var html = ArchDiagram.Site.Pages.DependenciesPage.Body(SampleModel(), maxNodes: 100);
        Assert.Contains("id=\"dep-internal\"", html);
        Assert.Contains("id=\"dep-external\"", html);
        Assert.Contains("id=\"dep-filter\"", html);
        Assert.Contains("landscape-filters", html); // reuses the existing filter-bar component
    }

    [Fact]
    public void Graph_page_emits_import_toggle_and_filter()
    {
        var html = ArchDiagram.Site.Pages.GraphPage.Body(SampleModel());
        Assert.Contains("id=\"g3d-imports\"", html);
        Assert.Contains("id=\"g3d-filter\"", html);
    }
}

public class GraphReducerTests
{
    private static List<DiagramNode> Nodes(int count) =>
        Enumerable.Range(0, count).Select(i => new DiagramNode($"id{i}", $"N{i}", "")).ToList();

    [Fact]
    public void Under_cap_passes_through_unchanged()
    {
        var nodes = Nodes(5);
        var (n, e) = GraphReducer.TrimToMax(nodes, [new DiagramEdge("id0", "id1")], 60);
        Assert.Equal(5, n.Count);
        Assert.Single(e);
    }

    [Fact]
    public void Over_cap_collapses_low_degree_nodes_into_aggregate()
    {
        var nodes = Nodes(100);
        // id0 is highly connected; the rest are leaves.
        var edges = Enumerable.Range(1, 99).Select(i => new DiagramEdge("id0", $"id{i}")).ToList();
        var (n, e) = GraphReducer.TrimToMax(nodes, edges, 10);

        Assert.Equal(10, n.Count);
        Assert.Contains(n, x => x.Id == GraphReducer.AggregateId);
        Assert.Contains(n, x => x.Id == "id0");
        // Collapsed edges are merged with a count label.
        var agg = e.Single(x => x.ToId == GraphReducer.AggregateId);
        Assert.Contains("links", agg.Label);
    }
}
