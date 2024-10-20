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
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    internal CancellationToken CancellationToken => _cancellationTokenSource.Token;

    internal event ErrorHandlerEvent? ErrorHandler;

    internal bool Running { get; private set; } = true;
    internal bool Started { get; private set; } = false;

    internal SqlProxyClientResponseMonitor(
       AsyncDuplexStreamingCall<SqlRequest, SqlResponse> stream
    )
    {
        _stream = stream;
    }

    internal async void Start()
    {
        if (Started) return;
        Started = true;
        try
        {
            await ReadStream();
        }
        catch (Exception ex)
        {
            TreatException(ex);
        }
        finally
        {
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
        _cancellationTokenSource.Cancel();
        Running = false;
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
    }

    private async Task ReadStream()
    {
        while (Running && await _stream.ResponseStream.MoveNext(_cancellationTokenSource.Token))
        {
            var response = _stream.ResponseStream.Current;
            if (response is not null && _responseHooks.TryGetValue(response.Id, out var hook))
            {
                await hook.WriteAsync(response);
            }
        }
    }

    internal void AddHook(string id, ChannelWriter<SqlResponse> writer) => _responseHooks.TryAdd(id, writer);
    internal void RemoveHook(string id) => _responseHooks.TryRemove(id, out _);
    public void Dispose()
    {
        Running = false;
        _cancellationTokenSource.Cancel();
    }
}