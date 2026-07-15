using ArchDiagram.Graph;
using ArchDiagram.Site;

namespace ArchDiagram.Tests;

public class GraphDataWriterTests
{
    [Theory]
    [InlineData("tests/Foo.cs", true)]
    [InlineData("src/Foo.Tests.cs", true)]
    [InlineData("web/util.spec.ts", true)]
    [InlineData("__tests__/a.js", true)]
    [InlineData("src/Bar.Test.cs", true)]
    [InlineData("specs/thing.py", true)]
    [InlineData("src/Program.cs", false)]
    [InlineData("web/util.ts", false)]
    [InlineData("latest/x.cs", false)]      // substring "test" must not match a folder
    [InlineData("src/contest.cs", false)]   // substring "test" in filename stem must not match
    public void IsTestFile_classifies_paths(string relPath, bool expected)
        => Assert.Equal(expected, GraphDataWriter.IsTestFile(relPath));

    [Fact]
    public void BuildJson_marks_test_nodes()
    {
        var model = new ProjectModel
        {
            RootName = "Sample",
            SourcePath = "C:/sample",
            Files =
            {
                new FileNode { RelPath = "src/Program.cs", Slug = "src_Program_cs", Language = "C#" },
                new FileNode { RelPath = "tests/ProgramTests.cs", Slug = "tests_ProgramTests_cs", Language = "C#" },
            },
            // one file-to-file dependency so BuildJson has something to plot
            FileDependencies = { new DepEdge { FromSlug = "tests_ProgramTests_cs", ToSlug = "src_Program_cs" } },
        };

        var json = GraphDataWriter.BuildJson(model);

        Assert.Contains("\"test\": true", json);
        Assert.Contains("\"test\": false", json);
    }
}
