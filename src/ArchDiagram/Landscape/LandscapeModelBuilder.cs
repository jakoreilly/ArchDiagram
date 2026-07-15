using ArchDiagram.Graph;

namespace ArchDiagram.Landscape;

/// <summary>Pure join logic: given the already-loaded sites, derive the cross-site
/// links. No filesystem access — fully unit-testable.</summary>
public static class LandscapeModelBuilder
{
    public static LandscapeModel Build(IReadOnlyList<SiteRef> sites)
    {
        var ordered = sites.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        return new LandscapeModel
        {
            Sites = ordered,
            Databases = BuildDatabases(ordered),
            PackageEdges = BuildPackageEdges(ordered),
            SharedPackages = BuildSharedPackages(ordered),
            ServiceCalls = BuildServiceCalls(ordered),
        };
    }

    private static List<SharedDb> BuildDatabases(List<SiteRef> sites)
    {
        var byHash = new Dictionary<string, SharedDb>(StringComparer.Ordinal);
        foreach (var s in sites)
        {
            foreach (var db in s.Model.Databases)
            {
                if (!byHash.TryGetValue(db.Hash, out var agg))
                {
                    agg = new SharedDb(db.Hash, db.Label, db.Server, db.Catalog, []);
                    byHash[db.Hash] = agg;
                }
                if (!agg.SiteIds.Contains(s.Id)) { agg.SiteIds.Add(s.Id); }
            }
        }
        foreach (var d in byHash.Values) { d.SiteIds.Sort(StringComparer.Ordinal); }
        return byHash.Values.OrderBy(d => d.Label, StringComparer.Ordinal).ThenBy(d => d.Hash, StringComparer.Ordinal).ToList();
    }

    private static List<PackageEdge> BuildPackageEdges(List<SiteRef> sites)
    {
        // project name -> site ids that PRODUCE it
        var producers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sites)
        {
            foreach (var p in s.Model.Projects)
            {
                if (!producers.TryGetValue(p.Name, out var list)) { producers[p.Name] = list = []; }
                if (!list.Contains(s.Id)) { list.Add(s.Id); }
            }
        }
        var edges = new HashSet<(string, string, string)>();
        foreach (var s in sites)
        {
            foreach (var p in s.Model.Projects)
            {
                foreach (var pkg in p.PackageReferences)
                {
                    if (!producers.TryGetValue(pkg, out var owners)) { continue; }
                    foreach (var owner in owners)
                    {
                        if (!owner.Equals(s.Id, StringComparison.Ordinal)) { edges.Add((s.Id, owner, pkg)); }
                    }
                }
            }
        }
        return edges.Select(e => new PackageEdge(e.Item1, e.Item2, e.Item3))
            .OrderBy(e => e.FromSiteId, StringComparer.Ordinal).ThenBy(e => e.ToSiteId, StringComparer.Ordinal)
            .ThenBy(e => e.Package, StringComparer.Ordinal).ToList();
    }

    private static List<SharedPackage> BuildSharedPackages(List<SiteRef> sites)
    {
        var producedNames = new HashSet<string>(
            sites.SelectMany(s => s.Model.Projects.Select(p => p.Name)), StringComparer.OrdinalIgnoreCase);
        var pkgToSites = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sites)
        {
            foreach (var pkg in s.Model.Projects.SelectMany(p => p.PackageReferences).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (producedNames.Contains(pkg)) { continue; } // that's a producer/consumer edge, not a shared external dep
                if (!pkgToSites.TryGetValue(pkg, out var set)) { pkgToSites[pkg] = set = new SortedSet<string>(StringComparer.Ordinal); }
                set.Add(s.Id);
            }
        }
        return pkgToSites.Where(kv => kv.Value.Count >= 2)
            .Select(kv => new SharedPackage(kv.Key, kv.Value.ToList()))
            .OrderBy(sp => sp.Name, StringComparer.Ordinal).ToList();
    }

    private static List<ServiceCallEdge> BuildServiceCalls(List<SiteRef> sites)
    {
        // simple type name -> site ids that DEFINE that type
        var definedIn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var s in sites)
        {
            foreach (var t in s.Model.Files.SelectMany(f => f.Types))
            {
                if (!definedIn.TryGetValue(t.Name, out var set)) { definedIn[t.Name] = set = new HashSet<string>(StringComparer.Ordinal); }
                set.Add(s.Id);
            }
        }
        var counts = new Dictionary<(string From, string To), int>();
        var samples = new Dictionary<(string From, string To), string>();
        foreach (var s in sites)
        {
            foreach (var c in s.Model.Calls)
            {
                if (!definedIn.TryGetValue(c.CalleeType, out var owners)) { continue; }
                foreach (var owner in owners)
                {
                    if (owner.Equals(s.Id, StringComparison.Ordinal)) { continue; } // intra-site call: ignore
                    var key = (s.Id, owner);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    samples.TryAdd(key, $"{c.CallerType}.{c.CallerMethod} → {c.CalleeType}.{c.CalleeMethod}");
                }
            }
        }
        return counts.Select(kv => new ServiceCallEdge(kv.Key.From, kv.Key.To, kv.Value, samples[kv.Key]))
            .OrderBy(e => e.FromSiteId, StringComparer.Ordinal).ThenBy(e => e.ToSiteId, StringComparer.Ordinal).ToList();
    }
}
