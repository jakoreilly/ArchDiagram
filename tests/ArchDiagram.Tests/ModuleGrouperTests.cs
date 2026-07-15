using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class ModuleGrouperTests
{
    private static FileNode File(string path, string ns = "") => new()
    {
        RelPath = path,
        Slug = path.Replace('/', '_').Replace('.', '_'),
        Language = "C#",
        Loc = 10,
        Types = ns.Length == 0 ? [] : [new TypeInfo { Name = "T", Kind = "class", Namespace = ns }],
    };

    [Fact]
    public void Folder_mode_aggregates_cross_module_edges_and_skips_self()
    {
        var a1 = File("A/One.cs");
        var a2 = File("A/Two.cs");
        var b1 = File("B/One.cs");
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { a1, a2, b1 },
            FileDependencies =
            {
                new DepEdge { FromSlug = a1.Slug, ToSlug = b1.Slug }, // A -> B
                new DepEdge { FromSlug = a1.Slug, ToSlug = a2.Slug }, // A -> A (self, skipped)
            },
        };

        var g = ModuleGrouper.Build(model);
        Assert.Equal("folder", g.Mode);
        Assert.Equal(1, g.Edges.GetValueOrDefault(("A", "B")));
        Assert.False(g.Edges.ContainsKey(("A", "A")));
        Assert.Equal(new[] { "A", "B" }, g.Modules.Select(m => m.Key).ToArray());
    }

    [Fact]
    public void Test_files_are_excluded_from_modules()
    {
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files =
            {
                File("src/A.cs"),
                new FileNode { RelPath = "tests/ATests.cs", Slug = "tests_atests_cs", Language = "C#", Loc = 10, IsTest = true },
            },
        };

        var g = ModuleGrouper.Build(model);
        Assert.DoesNotContain(g.Modules, m => m.Key.Contains("test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Counts_abstract_and_total_types()
    {
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files =
            {
                new FileNode { RelPath = "x/A.cs", Slug = "a", Language = "C#", Loc = 10,
                    Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "N" },
                             new TypeInfo { Name = "I", Kind = "interface", Namespace = "N" }] },
            },
        };
        var m = ModuleGrouper.Build(model).Modules.Single(m => m.Key == "N");
        Assert.Equal(2, m.TotalTypes);
        Assert.Equal(1, m.AbstractTypes);
    }

    [Fact]
    public void Namespace_mode_when_majority_have_namespaces()
    {
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { File("x/A.cs", "My.Core"), File("x/B.cs", "My.Core"), File("x/C.cs", "My.Web") },
        };

        var g = ModuleGrouper.Build(model);
        Assert.Equal("namespace", g.Mode);
        Assert.Contains(g.Modules, m => m.Key == "My.Core" && m.FileCount == 2);
    }
}
