namespace Codibre.GrpcSqlProxy.Client
{
    public interface ISqlProxyClient
    {
        ISqlProxyClientTunnel CreateChannel();
    }
}