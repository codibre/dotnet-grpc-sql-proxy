using System.Runtime.InteropServices.Marshalling;
using Codibre.GrpcSqlProxy.Api.Utils;
using Codibre.GrpcSqlProxy.Common;
using Dapper;
using Grpc.Core;
using static Codibre.GrpcSqlProxy.Api.SqlProxy;

namespace Codibre.GrpcSqlProxy.Api.Services;

public class SqlProxyService : SqlProxyBase
{
    public event ErrorHandlerEvent? ErrorHandler;

    public override async Task Run(
        IAsyncStreamReader<SqlRequest> requestStream,
        IServerStreamWriter<SqlResponse> responseStream,
        ServerCallContext context
    )
    {
        ProxyContext? proxyContext = null;
        try
        {
            while (await requestStream.MoveNext())
            {
                var request = requestStream.Current;
                await responseStream.Catch(request.Id, async () =>
                {
                    var connString = request.ConnString;
                    proxyContext?.Validate(responseStream, request);
                    proxyContext ??= await ProxyContext.GetConnection(responseStream, request);
                    if (proxyContext is not null)
                    {
                        if (request.PacketSize <= 0) request.PacketSize = 1000;
                        responseStream.PipeResponse(request, proxyContext);
                    }
                }, proxyContext);
            }
        }
        catch (Exception ex)
        {
            if (ErrorHandler is not null) ErrorHandler(this, ex);
        }
        finally
        {
            if (proxyContext is not null) await proxyContext.DisposeAsync();
        }
    }
}