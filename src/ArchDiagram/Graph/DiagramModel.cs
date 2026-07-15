namespace ArchDiagram.Graph;

public enum NodeShape { Box, Database, Rounded, Hexagon }

/// <summary>A node to draw. Id is any stable string; the renderer assigns the
/// mermaid-safe alias. Tooltip is plain text shown on hover (escaped here).</summary>
public sealed record DiagramNode(string Id, string Label, string Css, NodeShape Shape = NodeShape.Box, string Tooltip = "", string Href = "");

public sealed record DiagramEdge(string FromId, string ToId, string Label = "", bool Dashed = false);

/// <summary>Rendered mermaid text plus the alias->tooltip map site.js uses for hover
/// cards and the alias->href map it uses to make nodes clickable. <see cref="ShownNodes"/>
/// and <see cref="TotalNodes"/> drive the "showing N of M" trim banner; when equal the
/// diagram was not trimmed.</summary>
public sealed record Diagram(string Mermaid, Dictionary<string, string> Tooltips, Dictionary<string, string> Hrefs)
{
    public int ShownNodes { get; init; }
    public int TotalNodes { get; init; }
    public bool Trimmed => TotalNodes > ShownNodes;
}
