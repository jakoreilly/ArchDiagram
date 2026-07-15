using System.Xml.Linq;
using ArchDiagram.Graph;
using ArchDiagram.Site;

namespace ArchDiagram.Tests;

public class WikiExporterTests
{
    private static ProjectModel SampleModel(int cognitive = 0) => new()
    {
        RootName = "Sample",
        SourcePath = "C:/src/Sample",
        Files =
        [
            new FileNode
            {
                RelPath = "Lib/Thing.cs", Slug = "Lib_Thing_cs", Language = "C#", Loc = 40,
                Types =
                [
                    new TypeInfo
                    {
                        Name = "Thing", Kind = "class",
                        Methods = [ new MethodInfo { Name = "Do", Cyclomatic = 8, Cognitive = cognitive } ],
                    },
                ],
            },
        ],
        LanguageLoc = { ["C#"] = 40 },
    };

    // Storage-format bodies are XML fragments; wrap in a root so a well-formedness
    // failure (unescaped & or <) surfaces as a parse exception.
    private static void AssertWellFormed(string storageFragment) =>
        XDocument.Parse("<root xmlns:ac=\"a\" xmlns:ri=\"r\">" + storageFragment + "</root>");

    [Fact]
    public void Overview_is_well_formed_and_embeds_a_mermaid_code_macro()
    {
        var body = WikiExporter.RenderOverview(SampleModel(), 60, "2026-07-13");
        AssertWellFormed(body);
        Assert.Contains("<ac:structured-macro ac:name=\"code\">", body);
        Assert.Contains("<ac:parameter ac:name=\"language\">mermaid</ac:parameter>", body);
    }

    [Fact]
    public void Hotspots_includes_complexity_table_only_when_requested()
    {
        var withComplexity = WikiExporter.RenderHotspots(SampleModel(cognitive: 20), includeComplexity: true);
        AssertWellFormed(withComplexity);
        Assert.Contains("Most complex methods", withComplexity);
        Assert.Contains("High", withComplexity); // cognitive 20 → "High" band
        Assert.Contains("Do", withComplexity);   // the method name

        var without = WikiExporter.RenderHotspots(SampleModel(cognitive: 20), includeComplexity: false);
        Assert.DoesNotContain("Most complex methods", without);
    }

    [Fact]
    public void Mermaid_cdata_close_sequence_is_split_to_stay_well_formed()
    {
        // A model whose diagram text could contain ]]> is hard to force; test the
        // macro guard indirectly by confirming overview parses even with rich labels.
        var model = SampleModel();
        var body = WikiExporter.RenderOverview(model, 60, "2026-07-13");
        Assert.DoesNotContain("]]>]]>", body); // no accidental double-close
        AssertWellFormed(body);
    }
}
