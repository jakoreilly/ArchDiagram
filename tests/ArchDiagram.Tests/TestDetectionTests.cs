using ArchDiagram.Analysis;

namespace ArchDiagram.Tests;

public class TestDetectionTests
{
    [Theory]
    [InlineData("tests/Foo.cs")]
    [InlineData("src/Tests/Bar.cs")]
    [InlineData("MyProj.Tests/Baz.cs")]
    [InlineData("web/app.spec.ts")]
    [InlineData("web/app.test.js")]
    [InlineData("py/test_thing.py")]
    [InlineData("py/thing_test.py")]
    [InlineData("src/Foo.Tests.cs")]
    public void Detects_tests(string path) => Assert.True(TestDetection.IsTest(path));

    [Theory]
    [InlineData("src/Foo.cs")]
    [InlineData("src/Contest.cs")]        // substring, not a test
    [InlineData("src/Attest.cs")]
    [InlineData("app/Latest.ts")]
    public void Ignores_non_tests(string path) => Assert.False(TestDetection.IsTest(path));
}
