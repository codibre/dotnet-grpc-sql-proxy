using Codibre.GrpcSqlProxy.Api.Utils;
using Codibre.GrpcSqlProxy.Common;
using Dapper;
using Grpc.Core;
using static Codibre.GrpcSqlProxy.Api.SqlProxy;

namespace Codibre.GrpcSqlProxy.Api.Services
{
    public class SqlProxyService : SqlProxyBase
    {
        public event ErrorHandlerEvent? ErrorHandler;

        public override async Task Run(
            IAsyncStreamReader<SqlRequest> requestStream,
            IServerStreamWriter<SqlResponse> responseStream,
            ServerCallContext context
        )
        {
            var proxyContext = new ProxyContext();
            try
            {
                while (await requestStream.MoveNext())
                {
                    var request = requestStream.Current;
                    await responseStream.Catch(request.Id, async () =>
                    {
                        var connection = await responseStream.GetConnection(proxyContext, request);
                        if (connection is not null)
                        {
                            if (request.PacketSize <= 0) request.PacketSize = 1000;
                            responseStream.PipeResponse(connection, request, proxyContext);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                if (ErrorHandler is not null) ErrorHandler(this, ex);
            }
            finally
            {
                if (proxyContext.Transaction is not null)
                {
                    try
                    {
                        await proxyContext.Transaction.RollbackAsync();
                    }
                    catch
                    {
                        // Ignore if error occurs as no transaction were there
                    }
                }
                if (proxyContext.Connection is not null)
                    await proxyContext.Connection.CloseAsync();
            }
        }
    }
}