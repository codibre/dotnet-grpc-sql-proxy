using System.Data.Common;
using System.Transactions;
using Dapper;
using Grpc.Core;
using Microsoft.Data.SqlClient;
using static Dapper.SqlMapper;

namespace Codibre.GrpcSqlProxy.Api.Utils;

internal sealed class ProxyContext(string _connectionString) : IAsyncDisposable, IDisposable
{
    public string ConnectionString { get; set; } = _connectionString;
    private readonly SqlConnection _connection = new(_connectionString);
    private DbTransaction? _transaction { get; set; } = null;
    public int Index { get; set; } = 0;

    internal static async Task<ProxyContext?> GetConnection(
        IServerStreamWriter<SqlResponse> responseStream,
        SqlRequest request
    )
    {
        var connString = request.ConnString;
        if (string.IsNullOrWhiteSpace(connString))
        {
            responseStream.WriteError(request.Id, "Connection not established yet", 0);
            return null;
        }
        var result = new ProxyContext(connString);
        await result._connection.OpenAsync();
        return result;
    }

    internal bool Validate(IServerStreamWriter<SqlResponse> responseStream, SqlRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ConnString) && ConnectionString != request.ConnString)
        {
            responseStream.WriteError(request.Id, "ConnectionString differs from first one", Index);
            return false;
        }
        Index = 0;
        return true;
    }

    internal async Task StartTransaction() => _transaction = await _connection.BeginTransactionAsync();

    internal async ValueTask Commit()
    {
        await _transaction!.CommitAsync();
        _transaction = null;
    }

    internal async ValueTask Rollback()
    {
        if (_transaction is not null) await _transaction.RollbackAsync();
        _transaction = null;
    }

    internal IAsyncEnumerable<dynamic> Query(string sql, object? parameters)
        => _connection
            .QueryUnbufferedAsync(sql, parameters, _transaction);

    internal Task Execute(string sql, object? parameters)
        => _connection
            .ExecuteAsync(sql, parameters, _transaction);

    internal Task<GridReader> QueryMultiple(string sql, object? parameters)
        => _connection.QueryMultipleAsync(sql, parameters, _transaction);

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch
            {
                // Ignore if error occurs as no transaction were there
            }
        }
        if (_connection.State != System.Data.ConnectionState.Closed) await _connection.CloseAsync();
    }

    public void Dispose() => _ = DisposeAsync();
}