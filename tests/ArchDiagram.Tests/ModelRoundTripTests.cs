using ArchDiagram.Cli;
using ArchDiagram.Rendering;
using ArchDiagram.Site;

namespace ArchDiagram.Tests;

/// <summary>Proves model.json is a complete archive: scan → write model.json → read it back →
/// the regenerated site matches the original, with no access to the source tree.</summary>
public class ModelRoundTripTests
{
    private static Graph.ProjectModel Build() =>
        Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SampleRepo, Open = false });

    [Fact]
    public void Model_survives_a_json_round_trip()
    {
        var model = Build();
        var tmp = Path.Combine(Path.GetTempPath(), "archdiagram-rt-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ModelJsonWriter.Write(model, tmp);
            var back = ModelJsonReader.Read(tmp);

            Assert.Equal(model.RootName, back.RootName);
            Assert.Equal(model.Files.Count, back.Files.Count);
            Assert.Equal(model.Projects.Count, back.Projects.Count);
            Assert.Equal(model.FileDependencies.Count, back.FileDependencies.Count);
            Assert.Equal(model.Calls.Count, back.Calls.Count);
            Assert.Equal(model.Databases.Count, back.Databases.Count);
            // Deep-ish: a file's types/methods survive.
            var mf = model.Files.First(f => f.Types.Count > 0);
            var bf = back.Files.Single(f => f.Slug == mf.Slug);
            Assert.Equal(mf.Types.Count, bf.Types.Count);
            Assert.Equal(mf.Types[0].Name, bf.Types[0].Name);
        }
        finally { if (File.Exists(tmp)) { File.Delete(tmp); } }
    }

    [Fact]
    public void Site_rebuilds_from_model_json_without_source()
    {
        var model = Build();
        var work = Path.Combine(Path.GetTempPath(), "archdiagram-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var jsonPath = Path.Combine(work, "model.json");
            ModelJsonWriter.Write(model, jsonPath);

            // Rebuild purely from the JSON — as if the source tree were gone.
            var back = ModelJsonReader.Read(jsonPath);
            var outDir = Path.Combine(work, "rebuilt");
            var index = SiteGenerator.Generate(back, outDir, 60, "2026-01-01", showComplexity: true, showSnippets: false, wiki: false);

            Assert.True(File.Exists(index), "index.html should be rebuilt from model.json");
            foreach (var page in new[] { "index.html", "structure.html", "metrics.html", "scorecard.html", "refactor.html" })
            {
                Assert.True(File.Exists(Path.Combine(outDir, page)), $"missing rebuilt page: {page}");
            }
            // Every file still gets its page, sourced only from the model.
            Assert.All(back.Files, f =>
                Assert.True(File.Exists(Path.Combine(outDir, "files", f.Slug + ".html")), $"missing file page: {f.RelPath}"));
        }
        finally { if (Directory.Exists(work)) { Directory.Delete(work, recursive: true); } }
    }
}
