using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class LayeringAnalyzerTests
{
    private static FileNode F(string slug, string ns) => new()
    {
        RelPath = slug + ".cs", Slug = slug, Language = "C#", Loc = 10,
        Types = [new TypeInfo { Name = slug, Kind = "class", Namespace = ns }],
    };

    // Web -> Domain (allowed downward). Add Domain -> Web to force an upward violation.
    private static ProjectModel Model(bool addUpward)
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(F("w", "App.Web"));
        m.Files.Add(F("d", "App.Domain"));
        m.FileDependencies.Add(new DepEdge { FromSlug = "w", ToSlug = "d" });
        if (addUpward) { m.FileDependencies.Add(new DepEdge { FromSlug = "d", ToSlug = "w" }); }
        m.Layers.Add(new LayerDef("Presentation", ["App.Web"]));
        m.Layers.Add(new LayerDef("Domain", ["App.Domain"]));
        return m;
    }

    [Fact]
    public void Declared_downward_dependency_is_clean()
    {
        var r = LayeringAnalyzer.Analyze(Model(addUpward: false));
        Assert.True(r.Declared);
        Assert.Empty(r.Violations);
        Assert.Equal(2, r.Layers.Count);
    }

    [Fact]
    public void Declared_upward_dependency_is_a_violation()
    {
        var r = LayeringAnalyzer.Analyze(Model(addUpward: true));
        var v = Assert.Single(r.Violations);
        Assert.Equal("App.Domain", v.FromModule);
        Assert.Equal("Domain", v.FromLayer);
        Assert.Equal("App.Web", v.ToModule);
        Assert.Equal("Presentation", v.ToLayer);
    }

    [Fact]
    public void No_contract_infers_levels()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(F("a", "NA"));
        m.Files.Add(F("b", "NB"));
        m.FileDependencies.Add(new DepEdge { FromSlug = "a", ToSlug = "b" }); // a depends on b → a is a level above
        var r = LayeringAnalyzer.Analyze(m);
        Assert.False(r.Declared);
        Assert.Empty(r.Violations);
        // Two levels: NA (level 1, top) and NB (level 0, foundational).
        Assert.Equal(2, r.Layers.Count);
        Assert.Contains("NA", r.Layers[0].Modules); // top listed first
        Assert.Contains("NB", r.Layers[^1].Modules);
    }
}
