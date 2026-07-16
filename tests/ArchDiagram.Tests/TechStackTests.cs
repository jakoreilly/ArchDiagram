using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class TechStackTests
{
    [Fact]
    public void Recognises_frameworks_and_categorises_them()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Projects.Add(new CsprojInfo
        {
            Name = "Api", RelPath = "Api/Api.csproj",
            PackageReferences = ["Microsoft.AspNetCore.Mvc", "Microsoft.EntityFrameworkCore", "Serilog", "xunit"],
        });
        var stack = TechStack.Detect(m);
        Assert.Contains(stack, t => t.Name == "ASP.NET Core" && t.Category == "Web / API");
        Assert.Contains(stack, t => t.Name == "EF Core" && t.Category == "Data access");
        Assert.Contains(stack, t => t.Name == "Serilog" && t.Category == "Logging");
        Assert.Contains(stack, t => t.UsedBy.Contains("Api"));
    }

    [Fact]
    public void Unknown_packages_are_not_classified()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Projects.Add(new CsprojInfo { Name = "P", RelPath = "P/P.csproj", PackageReferences = ["Totally.Made.Up.Package"] });
        Assert.Empty(TechStack.Detect(m));
    }

    [Fact]
    public void Data_access_is_an_integration_category() =>
        Assert.Contains("Data access", TechStack.IntegrationCategories);
}
