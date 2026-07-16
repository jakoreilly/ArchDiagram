using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class ScorecardBuilderTests
{
    [Fact]
    public void Passing_signals_are_graded_ok()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        m.Files.Add(new FileNode { RelPath = "tests/ATests.cs", Slug = "at", Language = "C#", Loc = 60, IsTest = true });
        var card = ScorecardBuilder.Build(m);
        // No cycles, no committed secrets, no version drift, healthy test ratio → those signals pass.
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Dependency cycles"));
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Credentials in source"));
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Package version drift"));
        Assert.Equal(ScorecardBuilder.Status.Ok, Row(card, "Test-code ratio"));
    }

    [Fact]
    public void Todo_markers_in_test_files_do_not_count()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        m.Files.Add(new FileNode { RelPath = "tests/ScannerTests.cs", Slug = "t", Language = "C#", Loc = 60, IsTest = true,
            Todos = [new TodoItem(11, "TODO", "fix the widget"), new TodoItem(14, "BUG", "overflow")] });
        var card = ScorecardBuilder.Build(m);
        Assert.Equal("0", card.Rows.Single(r => r.Metric == "TODO / FIXME markers").Value);
    }

    private static ScorecardBuilder.Status Row(ScorecardBuilder.Card c, string metric) =>
        c.Rows.Single(r => r.Metric == metric).Status;

    [Fact]
    public void Embedded_credentials_fail_the_scorecard()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        m.Projects.Add(new CsprojInfo { Name = "Api", RelPath = "Api/Api.csproj",
            ConnectionStrings = [new DbUse { Hash = "h", Label = "db", HasCredential = true }] });
        var card = ScorecardBuilder.Build(m);
        Assert.Equal(ScorecardBuilder.Status.Fail, card.Overall);
        Assert.Contains(card.Rows, r => r.Metric == "Credentials in source" && r.Status == ScorecardBuilder.Status.Fail);
    }

    [Fact]
    public void Layering_is_na_without_a_contract()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 10,
            Types = [new TypeInfo { Name = "A", Kind = "class", Namespace = "NA" }] });
        var card = ScorecardBuilder.Build(m);
        Assert.Contains(card.Rows, r => r.Metric == "Layering violations" && r.Status == ScorecardBuilder.Status.NA);
    }
}
