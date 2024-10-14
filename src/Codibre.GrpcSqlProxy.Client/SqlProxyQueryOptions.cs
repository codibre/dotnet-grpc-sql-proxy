namespace Codibre.GrpcSqlProxy.Client;

public class SqlProxyQueryOptions()
{
    public bool? Compress { get; set; } = null;
    public int? PacketSize { get; set; } = null;

    public object? Params { get; set; } = null;
}