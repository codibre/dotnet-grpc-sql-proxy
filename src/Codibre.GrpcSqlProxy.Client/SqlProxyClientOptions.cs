using Microsoft.Extensions.Configuration;

namespace Codibre.GrpcSqlProxy.Client;

public class SqlProxyClientOptions(string url, string connectionString)
{
    public string SqlConnectionString { get; } = connectionString;
    public string Url { get; } = url;
    public bool Compress { get; set;}
    public int PacketSize { get; set; } = 1000;

    public SqlProxyClientOptions(IConfiguration configuration)
        : this(
            configuration.GetSection("GrpcSqlProxy").GetSection("Url").Value ?? throw new ArgumentException("No Proxy Url"),
            configuration.GetConnectionString("SqlConnection") ?? throw new ArgumentException("No connection string")
        )
    {
        Compress = configuration.GetSection("GrpcSqlProxy").GetSection("Compress").Value?.ToUpperInvariant() == "TRUE";
        PacketSize = int.Parse(configuration.GetSection("GrpcSqlProxy").GetSection("PacketSize").Value ?? "1000");
    }
}
