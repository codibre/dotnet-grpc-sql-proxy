using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using System.Transactions;
using Avro;
using Avro.Generic;
using Avro.IO;
using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client.Impl.Utils;
using Codibre.GrpcSqlProxy.Common;
using Google.Protobuf.Collections;
using Grpc.Core;

namespace Codibre.GrpcSqlProxy.Client.Impl;

public static class SqlProxyClientTunnelExtension
{
    public static ValueTask<T?> QueryFirstOrDefault<T>(
        this ISqlProxyClientTunnel tunnel,
        string sql,
        SqlProxyQueryOptions? options = null
    ) where T : class, new()
        => tunnel.Query<T>(sql, options).FirstOrDefaultAsync();

    public static ValueTask<T> QueryFirst<T>(
        this ISqlProxyClientTunnel tunnel,
        string sql,
        SqlProxyQueryOptions? options = null
    ) where T : class, new()
        => tunnel.Query<T>(sql, options).FirstAsync();


    internal static async ValueTask Complete<T>(this IAsyncEnumerable<T> stream)
    {
        await foreach (var _ in stream)
        {
            // Dummy
        }
    }
}