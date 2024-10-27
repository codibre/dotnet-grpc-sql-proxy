using Codibre.GrpcSqlProxy.Api;
using Grpc.Net.Client;

namespace Codibre.GrpcSqlProxy.Client.Impl;

public class GrpcSqlProxyClient(SqlProxyClientOptions options) : ISqlProxyClient
{
    private readonly AsyncLocal<ISqlProxyClientTunnel> _asyncLocal = new();
    private readonly SqlProxy.SqlProxyClient _client = new(GrpcChannel.ForAddress(options.Url));

    public ISqlProxyClientTunnel Channel
    {
        get
        {
            if (_asyncLocal.Value is null || _asyncLocal.Value.Disposed) return _asyncLocal.Value = CreateChannel();
            return _asyncLocal.Value;
        }
    }

    public ISqlProxyClientTunnel CreateChannel() => new SqlProxyClientTunnel(() => _client.Run(), options);

    public async ValueTask Initialize()
    {
        using var channel = CreateChannel();
        await channel.Noop();
    }
}