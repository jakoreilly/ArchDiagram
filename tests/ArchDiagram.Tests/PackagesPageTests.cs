using ArchDiagram.Graph;
using ArchDiagram.Site.Pages;

namespace ArchDiagram.Tests;

public class PackagesPageTests
{
    private static ProjectModel Model(params CsprojInfo[] projects)
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Projects.AddRange(projects);
        return m;
    }

    private static CsprojInfo Proj(string name, params (string, string)[] pkgs) => new()
    {
        Name = name, RelPath = name + "/" + name + ".csproj",
        Packages = pkgs.Select(p => new PackageRef(p.Item1, p.Item2)).ToList(),
    };

    [Fact]
    public void Flags_version_drift_across_projects()
    {
        var html = PackagesPage.Body(Model(
            Proj("Api", ("Serilog", "3.0.0"), ("Newtonsoft.Json", "13.0.1")),
            Proj("Worker", ("Serilog", "2.12.0"))));
        Assert.Contains("Version drift", html);
        Assert.Contains("Serilog", html);
        Assert.Contains("3.0.0", html);
        Assert.Contains("2.12.0", html);
    }

    [Fact]
    public void No_drift_when_versions_align()
    {
        var html = PackagesPage.Body(Model(
            Proj("Api", ("Serilog", "3.0.0")),
            Proj("Worker", ("Serilog", "3.0.0"))));
        Assert.Contains("No drift detected", html);
    }

    [Fact]
    public void Empty_state_when_no_packages()
    {
        Assert.Contains("No external", PackagesPage.Body(Model(Proj("Api"))));
    }
}
