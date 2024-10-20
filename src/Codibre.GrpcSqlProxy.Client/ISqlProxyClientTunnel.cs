using Codibre.GrpcSqlProxy.Common;

namespace Codibre.GrpcSqlProxy.Client;

public interface ISqlProxyClientTunnel : IDisposable
{
    ISqlProxyBatchQuery Batch { get; }
    ValueTask BeginTransaction();
    ValueTask Commit();
    ValueTask Rollback();
    ValueTask Execute(string sql, SqlProxyQueryOptions? options = null);
    IAsyncEnumerable<T> Query<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new();
    ValueTask<T?> QueryFirstOrDefault<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new();
    ValueTask<T> QueryFirst<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new();
    Reader QueryMultipleAsync(string sql, string[] schemas, SqlProxyQueryOptions? options);
    void OnError(ErrorHandlerEvent handler);
}