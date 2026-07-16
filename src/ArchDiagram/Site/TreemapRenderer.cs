using System.Globalization;
using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site;

/// <summary>Renders a deterministic, offline, theme-aware squarified treemap as inline SVG:
/// one rectangle per file, sized by lines of code, coloured by language, linking to the file
/// page. No JavaScript, no external assets — hover uses the native SVG &lt;title&gt;. Files are
/// pre-sorted (folder, size desc, path) so same-folder files cluster and output is stable.</summary>
public static class TreemapRenderer
{
    private const double Width = 1000, Height = 600;
    private const double MinLabelW = 46, MinLabelH = 14;

    private readonly record struct Rect(double X, double Y, double W, double H)
    {
        public double Shortest => Math.Min(W, H);
        public double Area => W * H;
    }

    private sealed record Item(FileNode File, double Value)
    {
        public double Area;   // assigned during layout (scaled to pixels)
    }

    private readonly record struct Placed(Item Item, Rect Rect);

    /// <summary>SVG markup for the treemap, or empty string when there is nothing to show.</summary>
    public static string Render(IReadOnlyList<FileNode> files)
    {
        var items = files
            .Where(f => f.Loc > 0 && !f.IsTest && !f.IsVendored)
            .OrderBy(f => TopFolder(f), StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(f => f.Loc)
            .ThenBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase)
            .Select(f => new Item(f, f.Loc))
            .ToList();
        if (items.Count == 0) { return ""; }

        var totalValue = items.Sum(i => i.Value);
        var scale = (Width * Height) / totalValue;
        foreach (var it in items) { it.Area = it.Value * scale; }

        var placed = new List<Placed>();
        Squarify(items, new Rect(0, 0, Width, Height), placed);

        return BuildSvg(placed);
    }

    // Squarified treemap (Bruls, Huizing, van Wijk). Packs rows whose rectangles stay as
    // close to square as possible, laying each fixed row along the current rectangle's
    // shortest side and recursing into the remainder.
    private static void Squarify(List<Item> items, Rect rect, List<Placed> output)
    {
        var remaining = new List<Item>(items);
        while (remaining.Count > 0)
        {
            var row = new List<Item>();
            var side = rect.Shortest;
            while (remaining.Count > 0)
            {
                var candidate = new List<Item>(row) { remaining[0] };
                if (row.Count > 0 && WorstRatio(candidate, side) > WorstRatio(row, side)) { break; }
                row.Add(remaining[0]);
                remaining.RemoveAt(0);
            }
            rect = LayoutRow(row, rect, output);
        }
    }

    /// <summary>Lays a fixed row along the shorter side of <paramref name="rect"/> and returns
    /// the leftover rectangle for the next rows.</summary>
    private static Rect LayoutRow(List<Item> row, Rect rect, List<Placed> output)
    {
        var rowArea = row.Sum(i => i.Area);
        var horizontal = rect.W >= rect.H;
        if (horizontal)
        {
            var rowW = rowArea / rect.H;
            var y = rect.Y;
            foreach (var it in row)
            {
                var h = it.Area / rowW;
                output.Add(new Placed(it, new Rect(rect.X, y, rowW, h)));
                y += h;
            }
            return new Rect(rect.X + rowW, rect.Y, rect.W - rowW, rect.H);
        }
        else
        {
            var rowH = rowArea / rect.W;
            var x = rect.X;
            foreach (var it in row)
            {
                var w = it.Area / rowH;
                output.Add(new Placed(it, new Rect(x, rect.Y, w, rowH)));
                x += w;
            }
            return new Rect(rect.X, rect.Y + rowH, rect.W, rect.H - rowH);
        }
    }

    /// <summary>Worst (largest) aspect ratio among a candidate row laid along <paramref name="side"/>.
    /// Lower is squarer; used to decide when to stop growing a row.</summary>
    private static double WorstRatio(List<Item> row, double side)
    {
        var sum = row.Sum(i => i.Area);
        if (sum <= 0 || side <= 0) { return double.MaxValue; }
        var max = row.Max(i => i.Area);
        var min = row.Min(i => i.Area);
        var side2 = side * side;
        var sum2 = sum * sum;
        return Math.Max((side2 * max) / sum2, sum2 / (side2 * min));
    }

    private static string BuildSvg(List<Placed> placed)
    {
        var sb = new StringBuilder();
        // Inline SVG (no xmlns needed in HTML5) so the page stays free of any http(s) URL.
        sb.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {Width:0} {Height:0}\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" aria-label=\"Codebase treemap: files sized by lines of code, coloured by language\">");
        foreach (var p in placed)
        {
            var f = p.Item.File;
            var fill = LanguagePalette.ColorFor(f.Language);
            var name = f.RelPath.Split('/')[^1];
            var title = $"{f.RelPath}\n{f.Language} · {f.Loc:N0} LOC";
            sb.Append(CultureInfo.InvariantCulture,
                $"<a href=\"files/{Html.Encode(f.Slug)}.html\"><rect x=\"{p.Rect.X:0.##}\" y=\"{p.Rect.Y:0.##}\" width=\"{p.Rect.W:0.##}\" height=\"{p.Rect.H:0.##}\" fill=\"{fill}\" stroke=\"var(--bg-panel)\" stroke-width=\"1\"><title>{Html.Encode(title)}</title></rect>");
            if (p.Rect.W >= MinLabelW && p.Rect.H >= MinLabelH)
            {
                var textColor = TextColorFor(fill);
                sb.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{p.Rect.X + 3:0.##}\" y=\"{p.Rect.Y + 11:0.##}\" font-size=\"9\" font-family=\"Segoe UI, sans-serif\" fill=\"{textColor}\">{Html.Encode(Clip(name, p.Rect.W))}</text>");
            }
            sb.Append("</a>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Black or white label depending on the fill's luminance (WCAG-ish), so text
    /// stays legible on any band in both themes.</summary>
    private static string TextColorFor(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#') { return "#fff"; }
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return luminance > 0.6 ? "#111" : "#fff";
    }

    private static string Clip(string name, double width)
    {
        var maxChars = (int)((width - 6) / 5.2);   // ~5.2px per char at font-size 9
        return maxChars >= name.Length || maxChars < 1 ? name : name[..Math.Max(1, maxChars)];
    }

    private static string TopFolder(FileNode f)
    {
        var idx = f.RelPath.IndexOf('/');
        return idx < 0 ? "" : f.RelPath[..idx];
    }
}
