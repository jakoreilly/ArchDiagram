using ArchDiagram.Graph;
using ArchDiagram.Scanning;
using ArchDiagram.Site.Pages;

namespace ArchDiagram.Tests;

public class ConfigSecretsPageTests
{
    [Theory]
    [InlineData("Server=db1;Database=orders;User Id=app;Password=secret;", true)]
    [InlineData("Server=db1;Database=orders;Integrated Security=SSPI;", false)]
    [InlineData("Data Source=db1;Initial Catalog=orders;Trusted_Connection=True;", false)]
    public void HasCredential_flags_embedded_secrets(string cs, bool expected)
        => Assert.Equal(expected, ConnectionStringNormalizer.HasCredential(cs));

    [Fact]
    public void Page_flags_credentials_in_source()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Projects.Add(new CsprojInfo
        {
            Name = "Api", RelPath = "Api/Api.csproj",
            ConnectionStrings =
            [
                new DbUse { Hash = "h1", Label = "orders", Server = "db1", Catalog = "orders",
                    Evidence = "Api/appsettings.json:3", HasCredential = true },
            ],
        });
        var html = ConfigSecretsPage.Body(m);
        Assert.Contains("Credentials in source", html);
        Assert.Contains("Api/appsettings.json:3", html);
        Assert.Contains("badge warn", html);
    }

    [Fact]
    public void Page_clean_when_no_embedded_credentials()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Projects.Add(new CsprojInfo
        {
            Name = "Api", RelPath = "Api/Api.csproj",
            ConnectionStrings = [new DbUse { Hash = "h1", Label = "orders", Server = "db1", Catalog = "orders", Evidence = "Api/appsettings.json:3", HasCredential = false }],
        });
        Assert.Contains("No connection string was found with an embedded", ConfigSecretsPage.Body(m));
    }
}
