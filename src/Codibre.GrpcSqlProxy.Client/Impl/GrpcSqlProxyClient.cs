using Codibre.GrpcSqlProxy.Api;
using Grpc.Net.Client;

namespace Codibre.GrpcSqlProxy.Client.Impl;

public class GrpcSqlProxyClient(SqlProxyClientOptions options) : ISqlProxyClient
{
    private readonly AsyncLocal<ISqlProxyClientTunnel> _asyncLocal = new();
    private readonly SqlProxy.SqlProxyClient _client = new(GrpcChannel.ForAddress(options.Url));

    public ISqlProxyClientTunnel Channel => _asyncLocal.Value ??= CreateChannel();

    public ISqlProxyClientTunnel CreateChannel() => new SqlProxyClientTunnel(() => _client.Run(), options);
}