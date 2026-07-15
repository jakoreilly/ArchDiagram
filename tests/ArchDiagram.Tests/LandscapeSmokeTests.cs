using ArchDiagram.Graph;
using ArchDiagram.Landscape;

namespace ArchDiagram.Tests;

public class LandscapeSmokeTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "archdiagram-landscape-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_outDir, recursive: true); } catch { }
    }

    private static ProjectModel Orders => new()
    {
        RootName = "orders", SourcePath = "x",
        Projects = [new CsprojInfo { Name = "Orders.Api", RelPath = "a.csproj",
            ConnectionStrings = [new DbUse { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }] }],
        Databases = [new DbNode { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }],
        Files = [new FileNode { RelPath = "OrdersController.cs", Slug = "o", Language = "C#" }],
    };

    private static ProjectModel Inventory => new()
    {
        RootName = "inventory", SourcePath = "y",
        Projects = [new CsprojInfo { Name = "Inventory.Api", RelPath = "b.csproj",
            ConnectionStrings = [new DbUse { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }] }],
        Databases = [new DbNode { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }],
        Files = [new FileNode { RelPath = "InventoryService.cs", Slug = "i", Language = "C#" }],
    };

    private void Generate()
    {
        var sites = new List<SiteRef>
        {
            new("site-orders", Orders, "../site-orders/index.html"),
            new("site-inventory", Inventory, "../site-inventory/index.html"),
        };
        var model = LandscapeModelBuilder.Build(sites);
        LandscapeGenerator.Generate(model, _outDir, maxNodes: 60, generatedOn: "2026-01-01");
    }

    [Fact]
    public void All_expected_pages_and_assets_exist()
    {
        Generate();
        foreach (var page in new[] { "index.html", "databases.html", "interconnections.html",
                                     Path.Combine("assets", "site.css"), Path.Combine("assets", "site.js"),
                                     Path.Combine("assets", "search-index.js"),
                                     Path.Combine("assets", "lib", "mermaid.min.js") })
        {
            Assert.True(File.Exists(Path.Combine(_outDir, page)), $"missing: {page}");
        }
    }

    [Fact]
    public void Overview_has_caveat_and_clickable_site_link()
    {
        Generate();
        var html = File.ReadAllText(Path.Combine(_outDir, "index.html"));
        Assert.Contains("appear as separate islands", html);
        Assert.Contains("../site-orders/index.html", html);
    }

    [Fact]
    public void Databases_page_marks_shared_db()
    {
        Generate();
        var html = File.ReadAllText(Path.Combine(_outDir, "databases.html"));
        Assert.Contains("shared", html); // both fixture sites use HASH_DB1
    }
}
