using ArchDiagram.Graph;
using ArchDiagram.Site.Pages;

namespace ArchDiagram.Tests;

public class CallsPageTests
{
    [Fact]
    public void Test_involved_calls_are_excluded_from_the_call_graph()
    {
        var code = new FileNode
        {
            RelPath = "src/Svc.cs", Slug = "svc", Language = "C#", Loc = 20,
            Types = [new TypeInfo { Name = "Svc", Kind = "class", Methods = [new MethodInfo { Name = "Do" }] }],
        };
        var test = new FileNode
        {
            RelPath = "tests/SvcTests.cs", Slug = "svctests", Language = "C#", Loc = 20, IsTest = true,
            Types = [new TypeInfo { Name = "SvcTests", Kind = "class", Methods = [new MethodInfo { Name = "DoTest" }] }],
        };
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { code, test },
            Calls =
            {
                new CallEdge { CallerSlug = "svctests", CallerType = "SvcTests", CallerMethod = "DoTest",
                               CalleeSlug = "svc", CalleeType = "Svc", CalleeMethod = "Do" },
            },
        };

        var html = CallsPage.Body(model, 60);
        // The only call is test → code, so the graph has no non-test edges to show.
        Assert.Contains("No cross-file method calls", html);
        Assert.DoesNotContain("SvcTests", html);
    }
}
