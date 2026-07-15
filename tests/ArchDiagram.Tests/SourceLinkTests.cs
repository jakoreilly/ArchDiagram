using ArchDiagram.Graph;

namespace ArchDiagram.Tests;

public class SourceLinkTests
{
    [Theory]
    [InlineData("github", "https://github.com/o/r", "main", "src/A.cs", 12, "https://github.com/o/r/blob/main/src/A.cs#L12")]
    [InlineData("gitlab", "https://gl.com/o/r", "dev", "src/A.cs", 0, "https://gl.com/o/r/-/blob/dev/src/A.cs")]
    [InlineData("github", "https://github.com/o/r/", "main", "/src/A.cs", 0, "https://github.com/o/r/blob/main/src/A.cs")]
    public void UrlFor_web(string t, string b, string r, string p, int line, string expected)
        => Assert.Equal(expected, new SourceLink { Type = t, Base = b, Ref = r }.UrlFor(p, line));

    [Fact]
    public void UrlFor_local_prefixes_file_scheme()
        => Assert.Equal("file:///C:/src/app/src/A.cs",
            new SourceLink { Type = "local", Base = "C:/src/app" }.UrlFor("src\\A.cs", 9));

    [Fact]
    public void UrlFor_empty_base_returns_empty()
        => Assert.Equal("", new SourceLink { Type = "github" }.UrlFor("A.cs", 1));

    [Fact]
    public void UrlFor_unknown_type_returns_empty()
        => Assert.Equal("", new SourceLink { Type = "none", Base = "x" }.UrlFor("A.cs", 1));
}
