namespace DocIntelApi.Hosting;

public sealed class QdrantOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 6334;
    public bool Https { get; init; }
    public string? ApiKey { get; init; }

    public static QdrantOptions From(IConfiguration configuration)
    {
        var host = configuration["Qdrant:Host"];
        if (string.IsNullOrWhiteSpace(host))
            host = "localhost";

        var port = 6334;
        var portValue = configuration["Qdrant:Port"];
        if (!string.IsNullOrWhiteSpace(portValue) && !int.TryParse(portValue, out port))
            port = 6334;

        var apiKey = configuration["Qdrant:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = null;

        var httpsSetting = configuration["Qdrant:Https"];
        bool https;
        if (!string.IsNullOrWhiteSpace(httpsSetting) && bool.TryParse(httpsSetting, out var parsed))
            https = parsed;
        else
            // Qdrant Cloud (API key present) uses TLS; local Compose uses plaintext gRPC.
            https = apiKey is not null;

        return new QdrantOptions
        {
            Host = host.Trim(),
            Port = port,
            Https = https,
            ApiKey = apiKey
        };
    }
}
