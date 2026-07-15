using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class ImportanceScorerTests
{
    private static FileNode File(string path, int loc = 10) =>
        new() { RelPath = path, Slug = path.Replace('/', '_').Replace('.', '_'), Language = "C#", Loc = loc };

    [Fact]
    public void Highest_fanin_ranks_first()
    {
        var core = File("src/Core.cs");
        var a = File("src/A.cs");
        var b = File("src/B.cs");
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { core, a, b },
            FileDependencies =
            {
                new DepEdge { FromSlug = a.Slug, ToSlug = core.Slug },
                new DepEdge { FromSlug = b.Slug, ToSlug = core.Slug },
            },
        };

        var ranked = ImportanceScorer.Rank(model);
        Assert.Equal(core.Slug, ranked[0].File.Slug);
        Assert.Equal(2, ranked[0].FanIn);
    }

    [Fact]
    public void Entry_point_is_recognised_and_scored()
    {
        var prog = File("src/Program.cs");
        var leaf = File("src/Leaf.cs");
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { prog, leaf },
            // Give the leaf a little fan-in so it scores; the entry point has none but the bonus.
            FileDependencies = { new DepEdge { FromSlug = prog.Slug, ToSlug = leaf.Slug } },
        };

        var ranked = ImportanceScorer.Rank(model);
        var progRow = ranked.Single(r => r.File.Slug == prog.Slug);
        Assert.True(progRow.EntryPoint);
        Assert.Equal("Application entry point", progRow.Reason);
    }

    [Fact]
    public void Excludes_test_files_from_ranking()
    {
        var code = File("src/Core.cs");
        var test = new FileNode { RelPath = "tests/CoreTests.cs", Slug = "tests_coretests_cs", Language = "C#", Loc = 200, IsTest = true };
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { code, test },
            FileDependencies = { new DepEdge { FromSlug = test.Slug, ToSlug = code.Slug } },
        };

        var ranked = ImportanceScorer.Rank(model);
        Assert.DoesNotContain(ranked, r => r.File.IsTest);
        Assert.Contains(ranked, r => r.File.Slug == code.Slug);
    }

    [Fact]
    public void Ordering_is_deterministic_on_ties()
    {
        var a = File("src/A.cs");
        var b = File("src/B.cs");
        var c = File("src/C.cs");
        // A and B both imported once → identical score; C imports them.
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { a, b, c },
            FileDependencies =
            {
                new DepEdge { FromSlug = c.Slug, ToSlug = a.Slug },
                new DepEdge { FromSlug = c.Slug, ToSlug = b.Slug },
            },
        };

        var first = ImportanceScorer.Rank(model).Select(r => r.File.Slug).ToList();
        var second = ImportanceScorer.Rank(model).Select(r => r.File.Slug).ToList();
        Assert.Equal(first, second);
        // A before B on the RelPath tiebreak.
        Assert.True(first.IndexOf(a.Slug) < first.IndexOf(b.Slug));
    }
}
