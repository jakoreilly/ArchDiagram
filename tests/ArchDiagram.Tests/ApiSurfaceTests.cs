using ArchDiagram.Analysis;
using ArchDiagram.Graph;
using ArchDiagram.Site.Pages;

namespace ArchDiagram.Tests;

public class ApiSurfaceTests
{
    private static FileNode File(string slug, params string[] deps)
    {
        var f = new FileNode { RelPath = slug + ".cs", Slug = slug, Language = "C#", Loc = 20 };
        foreach (var d in deps) { /* deps expressed via model edges below */ }
        return f;
    }

    // a -> b -> c chain (a is the entry point).
    private static ProjectModel Chain()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(File("a"));
        m.Files.Add(File("b"));
        m.Files.Add(File("c"));
        m.FileDependencies.Add(new DepEdge { FromSlug = "a", ToSlug = "b" });
        m.FileDependencies.Add(new DepEdge { FromSlug = "b", ToSlug = "c" });
        return m;
    }

    [Fact]
    public void Critical_path_traces_entry_to_target()
    {
        var path = CriticalPaths.ToFile(Chain(), "c");
        Assert.Equal(new[] { "a", "b", "c" }, path);
    }

    [Fact]
    public void Entry_point_has_no_inbound_path() => Assert.Null(CriticalPaths.ToFile(Chain(), "a"));

    [Fact]
    public void Api_surface_lists_public_types_and_public_methods_only()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode
        {
            RelPath = "src/Widget.cs", Slug = "w", Language = "C#", Loc = 30,
            Types =
            [
                new TypeInfo
                {
                    Name = "Widget", Kind = "class", Namespace = "Acme.Core", Modifiers = "public",
                    Methods =
                    [
                        new MethodInfo { Name = "Run", Signature = "void Run()", Modifiers = "public" },
                        new MethodInfo { Name = "Helper", Signature = "void Helper()", Modifiers = "private" },
                    ],
                },
                new TypeInfo { Name = "Secret", Kind = "class", Namespace = "Acme.Core", Modifiers = "internal" },
            ],
        });
        var html = ApiSurfacePage.Body(m);
        Assert.Contains("Widget", html);
        Assert.Contains("void Run()", html);
        Assert.DoesNotContain("void Helper()", html);   // private member excluded
        Assert.DoesNotContain(">Secret<", html);        // internal type excluded
        Assert.Contains("Acme.Core", html);
    }
}
