using ArchDiagram.Site;

namespace ArchDiagram.Tests;

public class GlossaryTests
{
    [Fact]
    public void Info_emits_a_button_for_a_known_term()
    {
        var html = Glossary.Info("instability");
        Assert.Contains("class=\"explain\"", html);
        Assert.Contains("data-term=\"instability\"", html);
    }

    [Fact]
    public void Info_is_empty_for_an_unknown_term() => Assert.Equal("", Glossary.Info("no-such-term"));

    [Fact]
    public void Json_contains_simple_and_detail_for_each_term()
    {
        var json = Glossary.Json();
        Assert.Contains("\"instability\"", json);
        Assert.Contains("simple", json);
        Assert.Contains("detail", json);
    }

    [Fact]
    public void Every_term_has_both_a_simple_and_a_detail_line()
    {
        Assert.All(Glossary.Terms.Values, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Simple));
            Assert.False(string.IsNullOrWhiteSpace(e.Detail));
        });
    }
}
