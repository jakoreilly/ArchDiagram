using ArchDiagram.Analysis;

namespace ArchDiagram.Tests;

public class VendoredDetectionTests
{
    [Theory]
    [InlineData("src/ArchDiagram/Site/assets/lib/mermaid.min.js", true)]
    [InlineData("assets/lib/3d-force-graph.min.js", true)]
    [InlineData("wwwroot/css/site.min.css", true)]
    [InlineData("app/main.bundle.js", true)]
    [InlineData("vendor/jquery/jquery.js", true)]
    [InlineData("third_party/foo/foo.js", true)]
    [InlineData("bower_components/x/x.js", true)]
    [InlineData("src/Program.cs", false)]
    [InlineData("web/app.ts", false)]          // real source, not minified
    [InlineData("src/mining.js", false)]        // "min" is incidental, not ".min.js"
    [InlineData("libraries/MyLib.cs", false)]   // "lib" as a word is not a vendor segment
    public void Classifies_vendored_assets(string relPath, bool expected)
        => Assert.Equal(expected, VendoredDetection.IsVendored(relPath));
}
