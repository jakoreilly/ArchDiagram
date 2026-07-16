namespace ArchDiagram.Analysis;

/// <summary>Heuristic: does a file look like a vendored / third-party / minified asset rather
/// than first-party source? These files (bundled libraries, minified JS/CSS) are large and
/// machine-generated — a single minified bundle can report tens of thousands of "lines" — so
/// they dominate size-based views and bury the project's own code. Conservative: matches
/// minified/bundle extensions and well-known vendor directory segments, not incidental names.</summary>
public static class VendoredDetection
{
    private static readonly string[] VendorDirSegments =
        ["vendor", "vendored", "third_party", "third-party", "thirdparty", "bower_components", "jspm_packages"];

    public static bool IsVendored(string relPath)
    {
        var segs = relPath.Split('/');
        for (var i = 0; i < segs.Length - 1; i++)
        {
            if (Array.IndexOf(VendorDirSegments, segs[i].ToLowerInvariant()) >= 0) { return true; }
        }

        var name = segs[^1].ToLowerInvariant();
        return name.EndsWith(".min.js") || name.EndsWith(".min.mjs") || name.EndsWith(".min.css")
            || name.EndsWith(".bundle.js") || name.EndsWith(".bundle.css");
    }
}
