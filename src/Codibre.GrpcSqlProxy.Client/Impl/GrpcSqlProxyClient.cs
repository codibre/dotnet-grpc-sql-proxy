using Codibre.GrpcSqlProxy.Api;
using Grpc.Net.Client;

namespace Codibre.GrpcSqlProxy.Client.Impl;

public class GrpcSqlProxyClient(SqlProxyClientOptions options) : ISqlProxyClient
{
    private readonly SqlProxy.SqlProxyClient _client = new(GrpcChannel.ForAddress(options.Url));
    public ISqlProxyClientTunnel CreateChannel() => new SqlProxyClientTunnel(_client.Run(), options);
}