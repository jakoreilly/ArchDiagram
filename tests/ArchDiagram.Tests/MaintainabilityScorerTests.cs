using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class MaintainabilityScorerTests
{
    [Fact]
    public void Small_simple_uncoupled_file_scores_high()
    {
        var s = MaintainabilityScorer.Score(loc: 40, maxCognitive: 2, coupling: 1);
        Assert.True(s >= 85, $"expected high score, got {s}");
        Assert.Equal(MaintainabilityScorer.Band.Good, MaintainabilityScorer.ToBand(s));
    }

    [Fact]
    public void Big_complex_coupled_file_scores_low()
    {
        var s = MaintainabilityScorer.Score(loc: 1200, maxCognitive: 45, coupling: 30);
        Assert.True(s <= 20, $"expected low score, got {s}");
        Assert.Equal(MaintainabilityScorer.Band.Poor, MaintainabilityScorer.ToBand(s));
    }

    [Fact]
    public void Score_is_clamped_and_ordered_worst_first()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode { RelPath = "src/Clean.cs", Slug = "c", Language = "C#", Loc = 30,
            Types = [new TypeInfo { Name = "C", Kind = "class", Namespace = "N",
                Methods = [new MethodInfo { Name = "M", Cognitive = 1 }] }] });
        m.Files.Add(new FileNode { RelPath = "src/Nasty.cs", Slug = "n", Language = "C#", Loc = 900,
            Types = [new TypeInfo { Name = "Nasty", Kind = "class", Namespace = "N",
                Methods = [new MethodInfo { Name = "Big", Cognitive = 40 }] }] });
        var ranked = MaintainabilityScorer.Rank(m);
        Assert.Equal("src/Nasty.cs", ranked[0].File.RelPath); // worst first
        Assert.All(ranked, r => Assert.InRange(r.Score, 0, 100));
    }
}
