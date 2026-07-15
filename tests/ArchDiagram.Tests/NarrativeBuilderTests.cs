using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class NarrativeBuilderTests
{
    private static ProjectModel Model(int todos = 0) => new()
    {
        RootName = "Widget",
        SourcePath = "C:/w",
        Files =
        {
            new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100,
                Todos = todos > 0 ? Enumerable.Range(0, todos).Select(i => new TodoItem(i + 1, "TODO", "x")).ToList() : [] },
            new FileNode { RelPath = "src/B.cs", Slug = "b", Language = "C#", Loc = 50 },
        },
        FileDependencies = { new DepEdge { FromSlug = "b", ToSlug = "a" } },
        LanguageLoc = { ["C#"] = 150 },
    };

    [Fact]
    public void ProjectSummary_names_primary_language_and_counts()
    {
        var s = NarrativeBuilder.ProjectSummary(Model());
        Assert.Contains("Widget is a C# codebase", s);
        Assert.Contains("2 file(s)", s);
        Assert.EndsWith(".", s);
    }

    [Fact]
    public void ProjectSummary_omits_todo_clause_when_zero()
    {
        Assert.DoesNotContain("TODO", NarrativeBuilder.ProjectSummary(Model(todos: 0)));
        Assert.Contains("TODO", NarrativeBuilder.ProjectSummary(Model(todos: 3)));
    }

    [Fact]
    public void ProjectSummary_has_no_dangling_conjunction()
    {
        var s = NarrativeBuilder.ProjectSummary(Model());
        Assert.DoesNotContain(" and .", s);
        Assert.DoesNotContain(", .", s);
        Assert.DoesNotContain("; ;", s);
    }

    [Fact]
    public void FolderBlurb_is_deterministic()
    {
        var files = Model().Files;
        Assert.Equal(NarrativeBuilder.FolderBlurb("src", files), NarrativeBuilder.FolderBlurb("src", files));
        Assert.Contains("src/ holds", NarrativeBuilder.FolderBlurb("src", files));
    }
}
