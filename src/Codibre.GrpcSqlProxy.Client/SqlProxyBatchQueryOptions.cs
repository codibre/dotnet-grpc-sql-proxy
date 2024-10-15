namespace Codibre.GrpcSqlProxy.Client;

public class SqlProxyBatchQueryOptions()
{
    public bool? Compress { get; set; } = null;
    public int? PacketSize { get; set; } = null;
}