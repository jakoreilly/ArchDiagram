using ArchDiagram.Cli;

namespace ArchDiagram.Tests;

public class AuthoredDescriptionsPipelineTests : IDisposable
{
    private readonly string _src = Path.Combine(Path.GetTempPath(), "archdiag-src", Guid.NewGuid().ToString("N"));

    public AuthoredDescriptionsPipelineTests()
    {
        Directory.CreateDirectory(Path.Combine(_src, "app"));
        File.WriteAllText(Path.Combine(_src, "app", "Foo.cs"), "namespace N; public class Foo { public void Bar() {} }");
        File.WriteAllText(Path.Combine(_src, "archdiagram.descriptions.json"), """
        { "project": "The widget engine.", "files": { "app/Foo.cs": "Hand-written note for Foo.", "app/": "The app layer." } }
        """);
    }

    public void Dispose() { try { Directory.Delete(_src, true); } catch { } }

    [Fact]
    public void Authored_descriptions_override_purpose_and_project()
    {
        var model = Pipeline.BuildModel(new CliOptions { SourcePath = _src, Open = false });

        Assert.Equal("The widget engine.", model.Description);
        var foo = model.Files.Single(f => f.RelPath == "app/Foo.cs");
        Assert.Equal("Hand-written note for Foo.", foo.Purpose);
        Assert.Equal("authored", foo.PurposeSource);
        Assert.Equal("The app layer.", model.FolderDescriptions["app"]);
    }
}
