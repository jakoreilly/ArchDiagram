using System.Text.Json;
using System.Text.RegularExpressions;
using ArchDiagram.Cli;
using ArchDiagram.Graph;
using ArchDiagram.Site;

namespace ArchDiagram.Tests;

public class SiteSmokeTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "archdiagram-tests", Guid.NewGuid().ToString("N"));
    private readonly ProjectModel _model;

    public SiteSmokeTests()
    {
        _model = Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SampleRepo, Open = false });
        SiteGenerator.Generate(_model, _outDir, maxNodes: 60, generatedOn: "2026-01-01");
    }

    public void Dispose()
    {
        try { Directory.Delete(_outDir, recursive: true); } catch { }
    }

    [Fact]
    public void All_expected_pages_and_assets_exist()
    {
        foreach (var page in new[] { "index.html", "guide.html", "structure.html", "dependencies.html", "types.html", "calls.html",
                                     "hotspots.html", "model.json", "ARCHITECTURE.md",
                                     Path.Combine("assets", "site.css"), Path.Combine("assets", "site.js"),
                                     Path.Combine("assets", "search-index.js"),
                                     Path.Combine("assets", "lib", "mermaid.min.js") })
        {
            Assert.True(File.Exists(Path.Combine(_outDir, page)), $"missing: {page}");
        }
    }

    [Fact]
    public void Every_file_node_has_a_page()
    {
        foreach (var f in _model.Files)
        {
            Assert.True(File.Exists(Path.Combine(_outDir, "files", f.Slug + ".html")), $"missing page for {f.RelPath}");
        }
    }

    [Fact]
    public void No_external_urls_in_generated_html()
    {
        foreach (var html in Directory.EnumerateFiles(_outDir, "*.html", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(html);
            Assert.DoesNotMatch(new Regex("(src|href)=\"https?://"), content);
        }
    }

    [Fact]
    public void All_internal_hrefs_resolve()
    {
        foreach (var html in Directory.EnumerateFiles(_outDir, "*.html", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(html)!;
            foreach (Match m in Regex.Matches(File.ReadAllText(html), "href=\"([^\"#]+)\""))
            {
                var target = Path.GetFullPath(Path.Combine(dir, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(target), $"{Path.GetFileName(html)} links to missing {m.Groups[1].Value}");
            }
        }
    }

    [Fact]
    public void Model_json_round_trips()
    {
        var json = File.ReadAllText(Path.Combine(_outDir, "model.json"));
        var back = JsonSerializer.Deserialize<ProjectModel>(json, Rendering.ModelJsonWriter.Options);
        Assert.NotNull(back);
        Assert.Equal(_model.Files.Count, back!.Files.Count);
        Assert.Equal(_model.Calls.Count, back.Calls.Count);
    }

    [Fact]
    public void Mermaid_blocks_are_wellformed_flowcharts()
    {
        foreach (var html in Directory.EnumerateFiles(_outDir, "*.html", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(html), "<pre class=\"mermaid-src\" hidden>([^<]*)</pre>"))
            {
                var text = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                Assert.StartsWith("flowchart ", text);
                // Balanced quotes per line keeps mermaid's parser happy.
                foreach (var line in text.Split('\n'))
                {
                    Assert.True(line.Count(c => c == '"') % 2 == 0, $"unbalanced quotes in: {line}");
                }
            }
        }
    }

    [Fact]
    public void Database_page_content_shows_label_not_hash()
    {
        var index = File.ReadAllText(Path.Combine(_outDir, "index.html"));
        Assert.Contains("orders", index);
        var db = _model.Databases.Single();
        Assert.DoesNotContain($"[(\"{db.Hash}", index);
    }

    [Fact]
    public void Search_index_is_valid_js_assignment_with_entries_for_every_file()
    {
        var js = File.ReadAllText(Path.Combine(_outDir, "assets", "search-index.js"));
        Assert.StartsWith("window.ARCH_SEARCH_INDEX = ", js);
        var json = js["window.ARCH_SEARCH_INDEX = ".Length..].TrimEnd().TrimEnd(';');
        var entries = JsonSerializer.Deserialize<List<string[]>>(json);
        Assert.NotNull(entries);
        foreach (var f in _model.Files)
        {
            Assert.Contains(entries!, e => e[0] == "file" && e[1] == f.RelPath && e[3] == $"files/{f.Slug}.html");
        }
        Assert.All(entries!, e => Assert.Equal(4, e.Length));
    }

    [Fact]
    public void Architecture_markdown_has_stats_and_mermaid()
    {
        var md = File.ReadAllText(Path.Combine(_outDir, "ARCHITECTURE.md"));
        Assert.Contains("## At a glance", md);
        Assert.Contains("```mermaid", md);
        Assert.Contains("flowchart ", md);
    }

    [Fact]
    public void Diagram_cards_carry_a_clickable_href_map()
    {
        var deps = File.ReadAllText(Path.Combine(_outDir, "dependencies.html"));
        Assert.Contains("class=\"hrefs\"", deps);
        // At least one node points at a real file page.
        Assert.Matches(new Regex("\"n\\d{3}\":\"files/[^\"]+\\.html\""), deps);
    }

    [Fact]
    public void Guide_page_is_linked_and_has_orientation_content()
    {
        var index = File.ReadAllText(Path.Combine(_outDir, "index.html"));
        Assert.Contains("href=\"guide.html\"", index);

        var guide = File.ReadAllText(Path.Combine(_outDir, "guide.html"));
        Assert.Contains("fan-in", guide);
        Assert.Contains("Ctrl", guide);
        Assert.Contains("class=\"legend\"", guide);
    }

    [Fact]
    public void Every_page_nav_links_to_the_guide()
    {
        foreach (var html in Directory.EnumerateFiles(_outDir, "*.html", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(html);
            // Root pages link "guide.html"; pages under files/ link "../guide.html".
            Assert.Matches(new Regex("href=\"(\\.\\./)?guide\\.html\""), content);
        }
    }

    [Fact]
    public void Graph_page_has_the_new_controls()
    {
        var graph = File.ReadAllText(Path.Combine(_outDir, "graph.html"));
        Assert.Contains("id=\"g3d-search\"", graph);
        Assert.Contains("id=\"g3d-hide-tests\"", graph);
        Assert.Contains("id=\"g3d-color\"", graph);
        Assert.Contains("id=\"g3d-degree-mode\"", graph);
    }

    [Fact]
    public void Graph_page_has_the_extended_controls()
    {
        var graph = File.ReadAllText(Path.Combine(_outDir, "graph.html"));
        foreach (var id in new[] { "g3d-spread", "g3d-isolate", "g3d-freeze", "g3d-hide-orphans", "g3d-degree-hide" })
        {
            Assert.Contains($"id=\"{id}\"", graph);
        }
        Assert.Contains("value=\"coupling\"", graph);
    }

    [Fact]
    public void Diagram_pages_link_to_the_3d_graph()
    {
        foreach (var page in new[] { "dependencies.html", "structure.html", "calls.html" })
        {
            var html = File.ReadAllText(Path.Combine(_outDir, page));
            Assert.Contains("3D Dependency Graph", html);
        }
    }

    [Fact]
    public void Overview_embeds_the_3d_graph()
    {
        var index = File.ReadAllText(Path.Combine(_outDir, "index.html"));
        Assert.Contains("id=\"graph3d-root\"", index);
        Assert.Contains("assets/lib/3d-force-graph.min.js", index);
    }

    [Fact]
    public void Overview_leads_with_graph_when_diagram_is_trivial()
    {
        // SampleRepo's project diagram is 3 nodes (2 projects + 1 db), so the interactive
        // graph leads and the static diagram is tucked into a details block below it.
        var index = File.ReadAllText(Path.Combine(_outDir, "index.html"));
        Assert.Contains("tucked below", index);
        Assert.DoesNotContain("<h2>Explore in 3D</h2>", index);
    }

    [Fact]
    public void Overview_stat_tiles_link_to_their_pages()
    {
        var index = File.ReadAllText(Path.Combine(_outDir, "index.html"));
        Assert.Contains("<a class=\"tile\" href=\"structure.html\">", index);
        Assert.Contains("<a class=\"tile\" href=\"types.html\">", index);
    }

    [Fact]
    public void Graph_page_has_the_type_legend_slot()
    {
        var graph = File.ReadAllText(Path.Combine(_outDir, "graph.html"));
        Assert.Contains("id=\"g3d-type-legend\"", graph);
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var second = _outDir + "-2";
        try
        {
            var model2 = Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SampleRepo, Open = false });
            SiteGenerator.Generate(model2, second, maxNodes: 60, generatedOn: "2026-01-01");
            foreach (var file in Directory.EnumerateFiles(_outDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(_outDir, file);
                var other = Path.Combine(second, rel);
                Assert.True(File.Exists(other), $"second run missing {rel}");
                Assert.True(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(other)), $"{rel} differs between runs");
            }
        }
        finally
        {
            try { Directory.Delete(second, recursive: true); } catch { }
        }
    }
}
