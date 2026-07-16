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
        Assert.Equal(13, model.Files.Count);
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
    public void Connection_strings_are_found_in_appsettings_and_config()
    {
        var model = Build();
        var lib = model.Projects.Single(p => p.Name == "Lib");
        // Real services keep connection strings in config, not .cs — both must be detected.
        Assert.Contains(lib.ConnectionStrings, u => u.Evidence.Contains("appsettings.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lib.ConnectionStrings, u => u.Evidence.Contains("web.config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Same_database_matches_across_key_alias_and_option_differences()
    {
        var model = Build();
        // App: "Server=db1;Database=orders;…" (.cs); Lib appsettings: "Data Source=db1;Initial
        // Catalog=orders;…"; Lib web.config: "Server=tcp:db1,1433;Database=orders;…". All point
        // at the same physical DB, so they must collapse to ONE node by canonical server+catalog.
        var db = Assert.Single(model.Databases);
        Assert.Equal("orders", db.Catalog);
        Assert.Equal("db1", db.Server);
    }

    [Fact]
    public void Web_config_xdt_transform_placeholders_are_ignored()
    {
        var model = Build();
        // App/Web.Release.config is an XDT transform whose connection string is a build-time
        // placeholder (Data Source=ReleaseSQLServer;Initial Catalog=MyReleaseDB). It must not
        // be mistaken for a real database.
        Assert.DoesNotContain(model.Databases, d =>
            d.Catalog.Equals("MyReleaseDB", StringComparison.OrdinalIgnoreCase)
            || d.Server.Equals("ReleaseSQLServer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(model.Projects.SelectMany(p => p.ConnectionStrings),
            u => u.Evidence.Contains("Web.Release.config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generated_site_output_is_not_scanned_as_source()
    {
        var model = Build();
        // SampleRepo/site-fake/ is a generated ArchDiagram site (model.json + assets/site.css).
        // It must be skipped, or its vendored assets (mermaid.min.js) and pages double-count.
        Assert.DoesNotContain(model.Files, f => f.RelPath.StartsWith("site-fake/", StringComparison.Ordinal));
    }

    [Fact]
    public void Connection_strings_in_comments_are_ignored()
    {
        var model = Build();
        // Lib/MathHelpers.cs has a connection-string example inside a // comment. It documents
        // the format; it is not a real database and must never be detected.
        Assert.DoesNotContain(model.Databases, d =>
            d.Server.Equals("commentbox", StringComparison.OrdinalIgnoreCase)
            || d.Catalog.Equals("commentcatalog", StringComparison.OrdinalIgnoreCase));
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
