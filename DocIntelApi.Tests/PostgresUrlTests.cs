using DocIntelApi.Hosting;
using Microsoft.Extensions.Configuration;

namespace DocIntelApi.Tests;

public class PostgresUrlTests
{
    [Fact]
    public void ToNpgsql_ConvertsUriAndRequiresSsl()
    {
        var cs = PostgresUrl.ToNpgsql(
            "postgres://docintel:s3cret%21@db.example.com:5432/docinteldb");

        Assert.Contains("Host=db.example.com", cs);
        Assert.Contains("Port=5432", cs);
        Assert.Contains("Database=docinteldb", cs);
        Assert.Contains("Username=docintel", cs);
        Assert.Contains("Password=s3cret!", cs);
        Assert.Contains("SSL Mode=Require", cs);
    }

    [Fact]
    public void ToNpgsql_HonorsSslModeQuery()
    {
        var cs = PostgresUrl.ToNpgsql(
            "postgresql://u:p@localhost/db?sslmode=disable");

        Assert.Contains("SSL Mode=Disable", cs);
    }

    [Fact]
    public void Resolve_PrefersConnectionStringOverDatabaseUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=from-cs",
                ["DATABASE_URL"] = "postgres://u:p@from-url/db"
            })
            .Build();

        Assert.Equal("Host=from-cs", PostgresUrl.Resolve(config));
    }

    [Fact]
    public void Resolve_UsesDatabaseUrlWhenConnectionStringEmpty()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "",
                ["DATABASE_URL"] = "postgres://u:p@from-url:5432/db"
            })
            .Build();

        var cs = PostgresUrl.Resolve(config);
        Assert.NotNull(cs);
        Assert.Contains("Host=from-url", cs);
        Assert.Contains("Username=u", cs);
    }
}

public class QdrantOptionsTests
{
    [Fact]
    public void From_DefaultsToLocalPlaintext()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var options = QdrantOptions.From(config);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(6334, options.Port);
        Assert.False(options.Https);
        Assert.Null(options.ApiKey);
    }

    [Fact]
    public void From_EnablesHttpsWhenApiKeySet()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Qdrant:Host"] = "xxx.cloud.qdrant.io",
                ["Qdrant:Port"] = "6334",
                ["Qdrant:ApiKey"] = "qk-test"
            })
            .Build();

        var options = QdrantOptions.From(config);
        Assert.Equal("xxx.cloud.qdrant.io", options.Host);
        Assert.True(options.Https);
        Assert.Equal("qk-test", options.ApiKey);
    }

    [Fact]
    public void From_HttpsFlagOverridesApiKeyHeuristic()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Qdrant:Https"] = "false",
                ["Qdrant:ApiKey"] = "qk-test"
            })
            .Build();

        Assert.False(QdrantOptions.From(config).Https);
    }
}
