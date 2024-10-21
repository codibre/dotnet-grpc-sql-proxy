using Codibre.GrpcSqlProxy.Common;

namespace Codibre.GrpcSqlProxy.Client;

public interface ISqlProxyClientTunnel : IDisposable
{
    ISqlProxyBatchQuery Batch { get; }
    void Start();
    ValueTask BeginTransaction();
    ValueTask Commit();
    ValueTask Rollback();
    ValueTask Execute(string sql, SqlProxyQueryOptions? options = null);
    IAsyncEnumerable<T> Query<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new();
    Reader QueryMultipleAsync(string sql, string[] schemas, SqlProxyQueryOptions? options);
    void OnError(ErrorHandlerEvent handler);
}