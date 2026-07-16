using ArchDiagram.Scanning;

namespace ArchDiagram.Tests;

public class ConnectionStringNormalizerTests
{
    private static string H(string cs) => ConnectionStringNormalizer.NormalizeAndHash(cs);

    [Fact]
    public void Same_server_and_catalog_match_despite_key_aliases()
    {
        // Server vs Data Source; Database vs Initial Catalog — same physical DB.
        Assert.Equal(
            H("Server=db1;Database=orders;User Id=a;Password=x;"),
            H("Data Source=db1;Initial Catalog=orders;Integrated Security=SSPI;"));
    }

    [Fact]
    public void Same_db_matches_despite_extra_options_and_protocol_port()
    {
        Assert.Equal(
            H("Server=db1;Database=orders;"),
            H("Server=tcp:db1,1433;Database=orders;Encrypt=True;MultipleActiveResultSets=true;"));
    }

    [Fact]
    public void Different_catalog_does_not_match()
    {
        Assert.NotEqual(
            H("Server=db1;Database=orders;"),
            H("Server=db1;Database=billing;"));
    }

    [Fact]
    public void Different_server_does_not_match()
    {
        Assert.NotEqual(
            H("Server=db1;Database=orders;"),
            H("Server=db2;Database=orders;"));
    }

    [Fact]
    public void Secrets_do_not_affect_identity()
    {
        Assert.Equal(
            H("Server=db1;Database=orders;User Id=a;Password=p1;"),
            H("Server=db1;Database=orders;User Id=b;Password=p2;"));
    }

    [Fact]
    public void Unparseable_strings_stay_distinct_via_fallback()
    {
        // No server/catalog extractable → fall back to full normalized pairs, so two
        // different option-only strings must not collapse together.
        Assert.NotEqual(
            H("Foo=1;Bar=2;"),
            H("Foo=3;Bar=4;"));
    }

    [Fact]
    public void Hash_is_sha256_hex()
    {
        Assert.Equal(64, H("Server=db1;Database=orders;").Length);
    }
}
