using Codibre.GrpcSqlProxy.Common;

namespace Codibre.GrpcSqlProxy.Client;

public interface ISqlProxyClientTunnel : IDisposable
{
    event ErrorHandlerEvent? ErrorHandler;
    ValueTask Execute(string sql);
    IAsyncEnumerable<T> Query<T>(string sql) where T: class, new();
    ValueTask<T?> QueryFirstOrDefault<T>(string sql) where T: class, new();
    ValueTask<T> QueryFirst<T>(string sql) where T: class, new();
}
