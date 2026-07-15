using ArchDiagram.Graph;

namespace ArchDiagram.Landscape;

/// <summary>One discovered site: its folder id, the loaded model, and a relative
/// href (from the landscape output dir) to that site's index.html.</summary>
public sealed record SiteRef(string Id, ProjectModel Model, string IndexHref);

/// <summary>A database seen in one or more sites, with the site ids that use it.</summary>
public sealed record SharedDb(string Hash, string Label, string Server, string Catalog, List<string> SiteIds);

/// <summary>A directed "consumer site references a package produced by producer site"
/// edge. Package is the matched project name.</summary>
public sealed record PackageEdge(string FromSiteId, string ToSiteId, string Package);

/// <summary>An external package (produced by no discovered site) shared by ≥2 sites.</summary>
public sealed record SharedPackage(string Name, List<string> SiteIds);

/// <summary>A heuristic cross-site call edge, aggregated with a sample + count.</summary>
public sealed record ServiceCallEdge(string FromSiteId, string ToSiteId, int Count, string Sample);

/// <summary>The whole federated view. All lists are pre-sorted for deterministic output.</summary>
public sealed record LandscapeModel
{
    public required List<SiteRef> Sites { get; init; }
    public List<SharedDb> Databases { get; init; } = [];        // every DB, matrix uses SiteIds
    public List<PackageEdge> PackageEdges { get; init; } = [];
    public List<SharedPackage> SharedPackages { get; init; } = [];
    public List<ServiceCallEdge> ServiceCalls { get; init; } = [];
    public List<string> Diagnostics { get; init; } = [];
}
