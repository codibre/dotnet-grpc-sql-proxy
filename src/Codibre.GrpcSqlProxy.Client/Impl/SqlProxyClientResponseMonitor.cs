using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Avro;
using Avro.Generic;
using Avro.IO;
using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client.Impl.Utils;
using Codibre.GrpcSqlProxy.Common;
using Google.Protobuf.Collections;
using Grpc.Core;

namespace Codibre.GrpcSqlProxy.Client.Impl;

internal sealed class SqlProxyClientResponseMonitor : IDisposable
{
    private readonly AsyncDuplexStreamingCall<SqlRequest, SqlResponse> _stream;
    internal readonly ConcurrentDictionary<string, ChannelWriter<SqlResponse>> _responseHooks = new();
    private CancellationTokenSource _cancellationTokenSource = new();

    internal CancellationToken CancellationToken => _cancellationTokenSource.Token;

    internal event ErrorHandlerEvent? ErrorHandler;
    internal bool Started { get; private set; } = false;

    internal SqlProxyClientResponseMonitor(
       AsyncDuplexStreamingCall<SqlRequest, SqlResponse> stream
    )
    {
        _stream = stream;
    }

    internal async void Start()
    {
        try
        {
            await ReadStream();
        }
        catch (Exception ex)
        {
            TreatException(ex);
            await CompleteStream();
        }
    }

    private void TreatException(Exception ex)
    {
        if (
            ex is not OperationCanceledException
            && ErrorHandler is not null
        ) ErrorHandler(this, ex);
    }

    private async Task CompleteStream()
    {
        _responseHooks.Clear();
        try
        {
            await _stream.RequestStream.CompleteAsync();
            _stream.Dispose();
        }
        catch (Exception)
        {
            // Ignoring errors due to already closed stream
        }
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource = new();
    }

    private async Task ReadStream()
    {
        if (Started) return;
        Started = true;
        while (await _stream.ResponseStream.MoveNext(_cancellationTokenSource.Token))
        {
            var response = _stream.ResponseStream.Current;
            if (response is not null && _responseHooks.TryGetValue(response.Id, out var hook))
            {
                await hook.WriteAsync(response);
                if (response.Last == LastEnum.Last)
                {
                    _responseHooks.TryRemove(response.Id, out _);
                    hook.Complete();
                }
            }
        }
        Started = false;
    }

    internal void AddHook(string id, ChannelWriter<SqlResponse> writer) => _responseHooks.TryAdd(id, writer);
    public void Dispose() => _ = CompleteStream();
}