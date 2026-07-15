using ArchDiagram.Cli;

namespace ArchDiagram.Tests;

public class PipelineTests
{
    private static Graph.ProjectModel Build() =>
        Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SampleRepo, Open = false });

    [Fact]
    public void Finds_all_fixture_files()
    {
        var model = Build();
        Assert.Equal(10, model.Files.Count);
        Assert.Contains(model.Files, f => f.RelPath == "App/Program.cs");
    }

    [Fact]
    public void Discovers_projects_and_references()
    {
        var model = Build();
        Assert.Equal(2, model.Projects.Count);
        var app = model.Projects.Single(p => p.Name == "App");
        Assert.Contains("Lib", app.ProjectReferenceNames);
        Assert.Contains("Newtonsoft.Json", app.PackageReferences);
        Assert.Equal("net10.0", app.TargetFramework);
    }

    [Fact]
    public void Database_label_is_catalog_name_not_hash()
    {
        var model = Build();
        var db = Assert.Single(model.Databases);
        Assert.Equal("orders", db.Label);
        Assert.Equal("db1", db.Server);
        Assert.Equal(64, db.Hash.Length);
    }

    [Fact]
    public void Connection_string_variable_name_is_captured()
    {
        var model = Build();
        var use = model.Projects.Single(p => p.Name == "App").ConnectionStrings.Single();
        Assert.Equal("ordersDb", use.VariableName);
    }

    [Fact]
    public void Resolves_csharp_ts_python_and_powershell_imports()
    {
        var model = Build();
        var bySlug = model.Files.ToDictionary(f => f.Slug, f => f.RelPath);
        var resolved = model.FileDependencies.Where(e => e.ToSlug.Length > 0)
            .Select(e => (From: bySlug[e.FromSlug], To: bySlug[e.ToSlug])).ToList();

        Assert.Contains(("App/Program.cs", "Lib/MathHelpers.cs"), resolved);
        Assert.Contains(("web/main.ts", "web/util.ts"), resolved);
        Assert.Contains(("scripts/tool.py", "scripts/helper.py"), resolved);
        Assert.Contains(("scripts/run.ps1", "scripts/lib.ps1"), resolved);
    }

    [Fact]
    public void Unresolved_imports_become_external_targets()
    {
        var model = Build();
        Assert.Contains(model.FileDependencies, e => e.ExternalTarget == "react");
    }

    [Fact]
    public void Extracts_types_methods_and_xml_summaries()
    {
        var model = Build();
        var helpers = model.Files.Single(f => f.RelPath == "Lib/MathHelpers.cs");
        var type = Assert.Single(helpers.Types);
        Assert.Equal("MathHelpers", type.Name);
        Assert.Equal("Sample.Lib", type.Namespace);
        Assert.Contains("Tiny math helpers", type.XmlSummary);
        Assert.Equal(2, type.Methods.Count);
        Assert.Contains(type.Methods, m => m.Name == "Double" && m.Arity == 1);
    }

    [Fact]
    public void Builds_cross_file_call_edge_by_name_and_arity()
    {
        var model = Build();
        Assert.Contains(model.Calls, c =>
            c.CallerType == "Program" && c.CallerMethod == "Main" &&
            c.CalleeType == "MathHelpers" && c.CalleeMethod == "Double" && !c.Ambiguous);
    }

    [Fact]
    public void Purpose_prefers_file_head_comment()
    {
        var model = Build();
        var program = model.Files.Single(f => f.RelPath == "App/Program.cs");
        Assert.Contains("Sample console app", program.Purpose);
        Assert.Equal("file-head comment", program.PurposeSource);
    }
}
