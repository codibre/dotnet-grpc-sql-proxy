using Codibre.GrpcSqlProxy.Common;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Data.SqlClient;

namespace Codibre.GrpcSqlProxy.Api.Utils;

public static class ResponseStreamExtensions
{
    private static async Task<SqlConnection> OpenConnection(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    internal static async Task<SqlConnection?> GetConnection(
        this IServerStreamWriter<SqlResponse> responseStream,
        ProxyContext context,
        SqlRequest request
    )
    {
        var connString = request.ConnString;
        if (string.IsNullOrWhiteSpace(connString))
        {
            if (context.Connection is null) responseStream.WriteError(request.Id, "Connection not established yet");
        }
        else
        {
            if (context.Connection is null)
            {
                context.ConnectionString = connString;
                context.Connection = await OpenConnection(connString);
            }
            if (context.ConnectionString is not null && context.ConnectionString != connString)
            {
                responseStream.WriteError(request.Id, "ConnectionString differs from first one");
                return null;
            }
        }
        return context.Connection;
    }

    public static async Task WriteSqlResponse(this IServerStreamWriter<SqlResponse> responseStream, SqlResponse response)
    {
        using var semaphore = await AsyncLock.Lock(responseStream);
        await responseStream.WriteAsync(response);
    }

    public static async Task Catch(this IServerStreamWriter<SqlResponse> responseStream, string id, Func<Task> callback)
    {
        try
        {
            await callback();
        }
        catch (Exception ex)
        {
            responseStream.WriteError(id, ex.Message);
        }
    }

    private static void WriteError(this IServerStreamWriter<SqlResponse> responseStream, string id, string message)
        => _ = responseStream.WriteSqlResponse(SqlResponseEx.CreateError(id, message));

    private static Task WriteSuccess(
        this IServerStreamWriter<SqlResponse> responseStream,
        SqlRequest request,
        (ByteString, bool) x)
    {
        return responseStream.WriteSqlResponse(
            SqlResponseEx.Create(
                request.Id,
                x.Item1,
                x.Item2
            )
        );
    }

    public static void PipeResponse(
        this IServerStreamWriter<SqlResponse> responseStream,
        SqlConnection connection,
        SqlRequest request
    ) => _ = responseStream.Catch(request.Id, () =>
            connection.GetResult(request)
                .ForEachAwaitAsync((x) => responseStream.WriteSuccess(request, x))
        );
}