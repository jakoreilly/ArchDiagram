namespace ArchDiagram.Analysis;

/// <summary>Heuristic: does a file look like automated-test code? Used to keep tests out of the
/// busy displays by default (they can be toggled back on in the viewer). Conservative — matches
/// test directory segments, <c>*.Tests</c> project folders, and common test file-name patterns,
/// but not incidental substrings (e.g. <c>Contest.cs</c> is not a test).</summary>
public static class TestDetection
{
    private static readonly string[] TestDirSegments =
        ["test", "tests", "__tests__", "spec", "specs", "e2e", "integrationtests", "unittests"];

    public static bool IsTest(string relPath)
    {
        var segs = relPath.Split('/');
        for (var i = 0; i < segs.Length - 1; i++)
        {
            var seg = segs[i].ToLowerInvariant();
            if (Array.IndexOf(TestDirSegments, seg) >= 0 || seg.EndsWith(".tests") || seg.EndsWith(".test")) { return true; }
        }

        var name = segs[^1];
        var lower = name.ToLowerInvariant();
        // C# convention is PascalCase FooTests/FooTest — the capital T is the word boundary that
        // keeps "Contest.cs"/"Attest.cs" out. Other languages use dot/underscore boundaries.
        return name.EndsWith("Tests.cs", StringComparison.Ordinal) || name.EndsWith("Test.cs", StringComparison.Ordinal)
            || lower.EndsWith(".spec.ts") || lower.EndsWith(".spec.js")
            || lower.EndsWith(".test.ts") || lower.EndsWith(".test.js")
            || lower.StartsWith("test_") || lower.EndsWith("_test.py");
    }
}
