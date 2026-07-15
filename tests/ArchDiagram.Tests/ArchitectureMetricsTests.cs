using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class ArchitectureMetricsTests
{
    private static FileNode F(string slug, string ns, string kind = "class") => new()
    {
        RelPath = slug + ".cs", Slug = slug, Language = "C#", Loc = 10,
        Types = [new TypeInfo { Name = slug, Kind = kind, Namespace = ns }],
    };

    // A → B → C chain (namespace modules NA, NB, NC).
    private static ProjectModel Chain() => new()
    {
        RootName = "R", SourcePath = "C:/r",
        Files = { F("a", "NA"), F("b", "NB"), F("c", "NC") },
        FileDependencies =
        {
            new DepEdge { FromSlug = "a", ToSlug = "b" },
            new DepEdge { FromSlug = "b", ToSlug = "c" },
        },
    };

    [Fact]
    public void Instability_is_ce_over_ca_plus_ce()
    {
        var r = ArchitectureMetrics.Compute(Chain());
        var byKey = r.Modules.ToDictionary(m => m.Key);
        Assert.Equal(1.0, byKey["NA"].Instability, 3);   // depends on B, nothing depends on it
        Assert.Equal(0.5, byKey["NB"].Instability, 3);   // Ca=1, Ce=1
        Assert.Equal(0.0, byKey["NC"].Instability, 3);   // stable sink
    }

    [Fact]
    public void Abstractness_and_distance_use_interfaces()
    {
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { F("a", "NA"), F("iface", "NB", kind: "interface") },
            FileDependencies = { new DepEdge { FromSlug = "a", ToSlug = "iface" } },
        };
        var r = ArchitectureMetrics.Compute(model);
        var nb = r.Modules.Single(m => m.Key == "NB");
        Assert.Equal(1.0, nb.Abstractness, 3);           // all-interface module
        // NB: I = 0 (stable), A = 1 → D = |1 + 0 - 1| = 0 (right on the main sequence).
        Assert.Equal(0.0, nb.Distance, 3);
    }

    [Fact]
    public void Propagation_cost_of_a_chain()
    {
        var r = ArchitectureMetrics.Compute(Chain());
        // reach sizes (incl self): A=3, B=2, C=1 → 6 / 3² = 0.666…
        Assert.Equal(6.0 / 9.0, r.PropagationCost, 3);
    }

    [Fact]
    public void No_cycle_when_acyclic() => Assert.Empty(ArchitectureMetrics.Compute(Chain()).Cycles);

    [Theory]
    [InlineData(1.0, 0.0, 0, ArchitectureMetrics.Zone.Healthy)]        // pure unstable leaf: D=0
    [InlineData(0.0, 0.0, 2, ArchitectureMetrics.Zone.ZoneOfPain)]     // stable concrete, has dependents
    [InlineData(0.0, 0.0, 0, ArchitectureMetrics.Zone.BenignLeaf)]     // stable concrete, no dependents
    [InlineData(1.0, 1.0, 0, ArchitectureMetrics.Zone.ZoneOfUselessness)]
    [InlineData(0.5, 0.0, 1, ArchitectureMetrics.Zone.Watch)]
    public void Classify_zones(double i, double a, int ca, ArchitectureMetrics.Zone expected)
        => Assert.Equal(expected, ArchitectureMetrics.Classify(i, a, ca));

    [Fact]
    public void Detects_a_cycle()
    {
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { F("a", "NA"), F("b", "NB") },
            FileDependencies =
            {
                new DepEdge { FromSlug = "a", ToSlug = "b" },
                new DepEdge { FromSlug = "b", ToSlug = "a" },
            },
        };
        var cycle = Assert.Single(ArchitectureMetrics.Compute(model).Cycles);
        Assert.Equal(new[] { "NA", "NB" }, cycle.ToArray());
    }
}
