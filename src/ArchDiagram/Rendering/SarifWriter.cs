using System.Text;
using System.Text.Json;
using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Rendering;

/// <summary>Exports the refactoring backlog and any failed scorecard signal as a SARIF 2.1.0
/// log, for code-scanning dashboards (GitHub code scanning, Azure DevOps, etc.). SARIF is
/// case-sensitive on every property name, so this uses its own JsonSerializerOptions rather
/// than <see cref="ModelJsonWriter.Options"/> (which camel-cases via a naming policy that would
/// corrupt SARIF's exact-cased schema keys like "$schema").</summary>
public static class SarifWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(ProjectModel model, string path)
    {
        var backlog = RefactoringBacklog.Build(model);
        var scorecard = ScorecardBuilder.Build(model);
        var bySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);

        var results = new List<object>();
        foreach (var item in backlog)
        {
            results.Add(BuildResult(item.Category, LevelFor(item.Severity), $"{item.Title} — {item.Why}", LocationFor(item.Link, bySlug)));
        }
        foreach (var row in scorecard.Rows.Where(r => r.Status == ScorecardBuilder.Status.Fail))
        {
            results.Add(BuildResult("Scorecard", "error", $"{row.Metric} = {row.Value} — {row.Note}", null));
        }

        // A C# identifier can't be "$schema" (SARIF's required top-level key), so the top level
        // is a Dictionary — its keys are arbitrary strings serialized verbatim — rather than an
        // anonymous type. This carries the exact "$schema" key with no post-serialization string
        // surgery, so no data-derived value can ever collide with a placeholder.
        var sarif = new Dictionary<string, object>
        {
            ["$schema"] = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new object[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "ArchDiagram",
                            informationUri = "https://github.com/",
                            rules = RulesFor(backlog),
                        },
                    },
                    results,
                },
            },
        };

        var json = JsonSerializer.Serialize(sarif, Options);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static object BuildResult(string ruleId, string level, string message, (string RelPath, int Line)? location)
    {
        if (location is null)
        {
            return new { ruleId, level, message = new { text = message } };
        }
        return new
        {
            ruleId,
            level,
            message = new { text = message },
            locations = new object[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = location.Value.RelPath },
                        region = new { startLine = Math.Max(1, location.Value.Line) },
                    },
                },
            },
        };
    }

    /// <summary>Best-effort file location from a backlog item's page link: "files/{slug}.html"
    /// resolves to that file's RelPath; a summary-page link (e.g. "metrics.html") has no single
    /// file, so the result carries no location — valid SARIF (locations is optional).</summary>
    private static (string RelPath, int Line)? LocationFor(string link, Dictionary<string, FileNode> bySlug)
    {
        const string prefix = "files/";
        const string suffix = ".html";
        if (!link.StartsWith(prefix, StringComparison.Ordinal) || !link.EndsWith(suffix, StringComparison.Ordinal)) { return null; }
        var slug = link[prefix.Length..^suffix.Length];
        return bySlug.TryGetValue(slug, out var f) ? (f.RelPath, 1) : null;
    }

    private static string LevelFor(RefactoringBacklog.Sev sev) => sev switch
    {
        RefactoringBacklog.Sev.Critical or RefactoringBacklog.Sev.High => "error",
        RefactoringBacklog.Sev.Medium => "warning",
        _ => "note",
    };

    private static object[] RulesFor(IReadOnlyList<RefactoringBacklog.Item> backlog) =>
        backlog.Select(i => i.Category).Distinct(StringComparer.Ordinal)
            .Append("Scorecard")
            .OrderBy(c => c, StringComparer.Ordinal)
            .Select(c => (object)new { id = c, name = c, shortDescription = new { text = c } })
            .ToArray();
}
