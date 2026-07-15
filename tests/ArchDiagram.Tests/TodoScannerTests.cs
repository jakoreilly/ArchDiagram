using ArchDiagram.Analysis;

namespace ArchDiagram.Tests;

public class TodoScannerTests
{
    [Fact]
    public void Finds_markers_across_comment_styles()
    {
        var todos = TodoScanner.Scan(
            "// TODO: fix the widget\n" +
            "# FIXME broken on Mondays\n" +
            "<!-- HACK: temporary -->\n" +
            "var x = 1; // BUG overflow at 2^31\n" +
            "normal line with no marker\n");

        Assert.Equal(4, todos.Count);
        Assert.Equal(("TODO", 1), (todos[0].Tag, todos[0].Line));
        Assert.Equal("fix the widget", todos[0].Text);
        Assert.Equal("FIXME", todos[1].Tag);
        Assert.Equal("HACK", todos[2].Tag);
        Assert.Equal("temporary", todos[2].Text);
        Assert.Equal(4, todos[3].Line);
    }

    [Fact]
    public void Ignores_identifiers_containing_marker_words()
    {
        Assert.Empty(TodoScanner.Scan("var todoList = GetTodoItems();\nDebugger.Break();\n"));
    }

    [Fact]
    public void Caps_markers_per_file()
    {
        var content = string.Concat(Enumerable.Repeat("// TODO: one more\n", 200));
        Assert.Equal(50, TodoScanner.Scan(content).Count);
    }
}
