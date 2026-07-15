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

    [Fact]
    public void Ignores_marker_at_line_start_when_not_a_comment()
    {
        // A YAML-style key or bare word at line start is not a comment (B2).
        Assert.Empty(TodoScanner.Scan("TODO: this is a yaml key not a comment\n", "YAML"));
    }

    [Fact]
    public void Finds_lowercase_bug_marker()
    {
        // B3: case-insensitive pre-filter no longer drops lowercase xxx/bug/undone.
        var todos = TodoScanner.Scan("// bug: leaks here\n", "C#");
        Assert.Single(todos);
        Assert.Equal("BUG", todos[0].Tag);
    }

    [Fact]
    public void Respects_language_comment_token()
    {
        // '#' is not a C# comment token, so a '#'-led marker is ignored for C#.
        Assert.Empty(TodoScanner.Scan("x = 1 # TODO not a c# comment\n", "C#"));
        Assert.Single(TodoScanner.Scan("# TODO real python comment\n", "Python"));
    }

    [Fact]
    public void Extracts_author_attribution()
    {
        var todos = TodoScanner.Scan("// TODO(alice): wire this up\n", "C#");
        Assert.Single(todos);
        Assert.Equal("alice", todos[0].Author);
        Assert.Equal("wire this up", todos[0].Text);
    }
}
