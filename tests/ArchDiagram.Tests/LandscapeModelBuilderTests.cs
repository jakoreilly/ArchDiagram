using ArchDiagram.Graph;
using ArchDiagram.Landscape;

namespace ArchDiagram.Tests;

public class LandscapeModelBuilderTests
{
    private static SiteRef Site(string id, ProjectModel m) => new(id, m, $"../{id}/index.html");

    private static readonly ProjectModel Orders = new()
    {
        RootName = "orders", SourcePath = "x",
        Projects = [new CsprojInfo { Name = "Orders.Api", RelPath = "a.csproj",
            PackageReferences = ["Shared.Contracts", "Serilog"],
            ConnectionStrings = [new DbUse { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }] }],
        Databases = [new DbNode { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }],
        Files = [new FileNode { RelPath = "OrdersController.cs", Slug = "o", Language = "C#",
            Types = [new TypeInfo { Name = "OrdersController", Kind = "class",
                Methods = [new MethodInfo { Name = "Get" }] }] }],
        Calls = [new CallEdge { CallerSlug = "o", CallerType = "OrdersController", CallerMethod = "Get",
            CalleeSlug = "?", CalleeType = "InventoryService", CalleeMethod = "Check" }],
    };

    private static readonly ProjectModel Inventory = new()
    {
        RootName = "inventory", SourcePath = "y",
        Projects = [new CsprojInfo { Name = "Shared.Contracts", RelPath = "b.csproj",
            PackageReferences = ["Serilog"],
            ConnectionStrings = [new DbUse { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }] }],
        Databases = [new DbNode { Hash = "HASH_DB1", Label = "orders", Server = "db1", Catalog = "orders" }],
        Files = [new FileNode { RelPath = "InventoryService.cs", Slug = "i", Language = "C#",
            Types = [new TypeInfo { Name = "InventoryService", Kind = "class" }] }],
    };

    private static LandscapeModel Build() =>
        LandscapeModelBuilder.Build([Site("site-orders", Orders), Site("site-inventory", Inventory)]);

    [Fact]
    public void Shared_database_lists_both_sites()
    {
        var db = Assert.Single(Build().Databases);
        Assert.Equal("HASH_DB1", db.Hash);
        Assert.Equal(["site-inventory", "site-orders"], db.SiteIds);
    }

    [Fact]
    public void Package_edge_points_consumer_to_producer()
    {
        var e = Assert.Single(Build().PackageEdges);
        Assert.Equal("site-orders", e.FromSiteId);     // Orders.Api references Shared.Contracts
        Assert.Equal("site-inventory", e.ToSiteId);    // produced by the inventory site
        Assert.Equal("Shared.Contracts", e.Package);
    }

    [Fact]
    public void Shared_external_package_detected()
    {
        var sp = Assert.Single(Build().SharedPackages);
        Assert.Equal("Serilog", sp.Name);              // both sites reference it, no site produces it
    }

    [Fact]
    public void Cross_service_call_crosses_sites_only()
    {
        var c = Assert.Single(Build().ServiceCalls);
        Assert.Equal("site-orders", c.FromSiteId);     // OrdersController.Get → InventoryService.Check
        Assert.Equal("site-inventory", c.ToSiteId);
        Assert.Equal(1, c.Count);
    }
}
