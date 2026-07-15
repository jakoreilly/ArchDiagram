using ArchDiagram.Analysis;

namespace ArchDiagram.Tests;

public class DescriptionsLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "archdiag-desc", Guid.NewGuid().ToString("N"));

    public DescriptionsLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string json)
    {
        var p = Path.Combine(_dir, DescriptionsLoader.DefaultFileName);
        File.WriteAllText(p, json);
        return p;
    }

    [Fact]
    public void Loads_project_files_and_folders()
    {
        Write("""
        { "project": "A tool.", "files": { "src/A.cs": "does A", "src/Sub/": "sub area" } }
        """);
        var diags = new List<string>();
        var d = DescriptionsLoader.Load(null, _dir, diags);

        Assert.Equal("A tool.", d.Project);
        Assert.Equal("does A", d.Files["src/A.cs"]);
        Assert.Equal("sub area", d.Folders["src/Sub"]);
        Assert.Empty(diags);
    }

    [Fact]
    public void Missing_default_file_is_empty_without_diagnostic()
    {
        var diags = new List<string>();
        var d = DescriptionsLoader.Load(null, _dir, diags);
        Assert.True(d.IsEmpty);
        Assert.Empty(diags);
    }

    [Fact]
    public void Explicit_missing_path_adds_diagnostic()
    {
        var diags = new List<string>();
        var d = DescriptionsLoader.Load(Path.Combine(_dir, "nope.json"), _dir, diags);
        Assert.True(d.IsEmpty);
        Assert.Single(diags);
    }

    [Fact]
    public void Malformed_json_adds_diagnostic_and_returns_empty()
    {
        var p = Write("{ not valid json ");
        var diags = new List<string>();
        var d = DescriptionsLoader.Load(p, _dir, diags);
        Assert.True(d.IsEmpty);
        Assert.Single(diags);
    }
}
