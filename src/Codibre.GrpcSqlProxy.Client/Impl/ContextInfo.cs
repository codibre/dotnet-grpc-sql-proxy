using Codibre.GrpcSqlProxy.Api;
using Grpc.Core;

namespace Codibre.GrpcSqlProxy.Client.Impl;

internal class ContextInfo : IDisposable
{
    private readonly Action _clear;
    private readonly ExecutionContext? _executionContext;
    public bool Disposed { get; private set; } = false;
    public bool Transaction { get; set; } = false;
    public AsyncDuplexStreamingCall<SqlRequest, SqlResponse> Stream { get; }
    public SqlProxyClientResponseMonitor Monitor { get; }
    public ContextInfo(
        Func<AsyncDuplexStreamingCall<SqlRequest, SqlResponse>> getStream,
        Action clear
    )
    {
        Stream = getStream();
        Monitor = new(Stream);
        _clear = clear;
        _executionContext = ExecutionContext.Capture();
    }

    public void Dispose()
    {
        Monitor.Dispose();
        Disposed = true;
        if (_executionContext is not null) ExecutionContext.Run(_executionContext, (_) => _clear(), null);
    }
}