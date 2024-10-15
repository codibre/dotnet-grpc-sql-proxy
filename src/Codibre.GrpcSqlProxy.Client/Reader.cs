using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client.Impl;
using Codibre.GrpcSqlProxy.Common;

namespace Codibre.GrpcSqlProxy.Client;

public class Reader(
    IAsyncEnumerable<SqlResponse> asyncEnumerable
)
{
    private readonly IAsyncEnumerator<SqlResponse> _asyncEnumerator = asyncEnumerable.GetAsyncEnumerator();

    public async IAsyncEnumerable<SqlResponse> GetSingleSet()
    {
        while (await _asyncEnumerator.MoveNextAsync())
        {
            var current = _asyncEnumerator.Current;
            yield return current;
            if (current.Last == LastEnum.Last) IsConsumed = true;
            if (current.Last == LastEnum.SetLast) break;
        }
    }

    public bool IsConsumed { get; private set; } = false;

    public IAsyncEnumerable<T> ReadAsync<T>()
    where T : class, new()
    {
        var type = typeof(T);
        return SqlProxyClientTunnel.ConvertResult<T>(type, type.GetCachedSchema(), GetSingleSet());
    }

    public async ValueTask<T?> ReadFirstOrDefaultAsync<T>()
    where T : class, new()
    {
        var first = true;
        T? current = null;
        await ReadAsync<T>().ForEachAsync((x) =>
        {
            if (first)
            {
                current = x;
                first = false;
            }
        });
        return current;
    }

    public async ValueTask<T> ReadFirstAsync<T>()
    where T : class, new()
        => await ReadFirstOrDefaultAsync<T>() ?? throw new SqlProxyException("Result is empty");
}