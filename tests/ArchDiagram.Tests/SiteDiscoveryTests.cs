using System.Text.Json;
using ArchDiagram.Graph;
using ArchDiagram.Landscape;

namespace ArchDiagram.Tests;

public class SiteDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "archdiagram-discovery-tests", Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WriteSite(string folder, string rootName)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var model = new ProjectModel { RootName = rootName, SourcePath = "x" };
        File.WriteAllText(Path.Combine(dir, "model.json"), JsonSerializer.Serialize(model, WriteOptions));
    }

    [Fact]
    public void Discover_without_filter_returns_all_sites()
    {
        WriteSite("site-a", "a");
        WriteSite("site-b", "b");
        WriteSite("site-c", "c");

        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), new List<string>());

        Assert.Equal(new[] { "site-a", "site-b", "site-c" }, sites.Select(s => s.Id).OrderBy(x => x));
    }

    [Fact]
    public void Discover_with_only_filters_to_named_subset()
    {
        WriteSite("site-a", "a");
        WriteSite("site-b", "b");
        WriteSite("site-c", "c");

        var only = new HashSet<string>(new[] { "site-a", "site-c" }, StringComparer.OrdinalIgnoreCase);
        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), new List<string>(), only);

        Assert.Equal(new[] { "site-a", "site-c" }, sites.Select(s => s.Id).OrderBy(x => x));
    }
}
