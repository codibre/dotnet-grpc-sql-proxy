using Codibre.GrpcSqlProxy.Common;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Data.SqlClient;

namespace Codibre.GrpcSqlProxy.Api.Utils;

public static class ResponseStreamExtensions
{

    public static async Task WriteSqlResponse(this IServerStreamWriter<SqlResponse> responseStream, SqlResponse response)
    {
        using var semaphore = await AsyncLock.Lock(responseStream);
        await responseStream.WriteAsync(response);
    }

    internal static async Task Catch(this IServerStreamWriter<SqlResponse> responseStream, string id, Func<Task> callback, ProxyContext? context)
    {
        try
        {
            await callback();
        }
        catch (Exception ex)
        {
            responseStream.WriteError(id, ex.Message, context?.Index ?? 0);
        }
    }

    internal static void WriteError(this IServerStreamWriter<SqlResponse> responseStream, string id, string message, int index)
        => _ = responseStream.WriteSqlResponse(SqlResponseEx.CreateError(id, message, index));

    private static Task WriteSuccess(
        this IServerStreamWriter<SqlResponse> responseStream,
        SqlRequest request,
        QueryPacket packet)
    {
        return responseStream.WriteSqlResponse(
            SqlResponseEx.Create(
                request.Id,
                packet
            )
        );
    }

    internal static void PipeResponse(
        this IServerStreamWriter<SqlResponse> responseStream,
        SqlRequest request,
        ProxyContext context
    ) => _ = responseStream.Catch(request.Id, () =>
            context.GetResult(request)
                .ForEachAwaitAsync((x) => responseStream.WriteSuccess(request, x)),
            context
        );
}