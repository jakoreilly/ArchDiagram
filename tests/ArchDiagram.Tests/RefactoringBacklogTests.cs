using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class RefactoringBacklogTests
{
    [Fact]
    public void Embedded_credentials_are_a_critical_item()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Projects.Add(new CsprojInfo { Name = "Api", RelPath = "Api/Api.csproj",
            ConnectionStrings = [new DbUse { Hash = "h", Label = "db", HasCredential = true }] });
        var items = RefactoringBacklog.Build(m);
        var sec = Assert.Single(items, i => i.Category == "Security");
        Assert.Equal(RefactoringBacklog.Sev.Critical, sec.Severity);
        Assert.False(string.IsNullOrWhiteSpace(sec.Tip));
    }

    [Fact]
    public void Items_are_ordered_by_severity()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        // a poor-maintainability file (Medium/High) + many todos (Low) → ensure ordering.
        m.Files.Add(new FileNode { RelPath = "src/Big.cs", Slug = "b", Language = "C#", Loc = 1500,
            Types = [new TypeInfo { Name = "Big", Kind = "class", Namespace = "N",
                Methods = [new MethodInfo { Name = "M", Cognitive = 40 }] }],
            Todos = Enumerable.Range(0, 20).Select(i => new TodoItem(i, "TODO", "x")).ToList() });
        var items = RefactoringBacklog.Build(m);
        Assert.NotEmpty(items);
        for (var i = 1; i < items.Count; i++)
        {
            Assert.True((int)items[i - 1].Severity <= (int)items[i].Severity, "not severity-ordered");
        }
    }

    [Fact]
    public void Clean_model_has_no_items()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 30,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "N" }] });
        Assert.Empty(RefactoringBacklog.Build(m));
    }
}
