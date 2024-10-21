namespace Codibre.GrpcSqlProxy.Client;

public interface ISqlProxyClient
{
    ISqlProxyClientTunnel Channel { get; }
    ISqlProxyClientTunnel CreateChannel();
}