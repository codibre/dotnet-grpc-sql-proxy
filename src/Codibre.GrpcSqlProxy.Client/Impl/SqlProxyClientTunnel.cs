using System.Collections.Concurrent;
using System.Threading.Channels;
using Avro.Generic;
using Avro.IO;
using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client.Impl.Utils;
using Codibre.GrpcSqlProxy.Common;
using Grpc.Core;

namespace Codibre.GrpcSqlProxy.Client.Impl;

public sealed class SqlProxyClientTunnel(AsyncDuplexStreamingCall<SqlRequest, SqlResponse> stream, SqlProxyClientOptions options) : ISqlProxyClientTunnel
{
    private readonly ConcurrentDictionary<string, ChannelWriter<SqlResponse>> _responseHooks = new();
    private readonly AsyncDuplexStreamingCall<SqlRequest, SqlResponse> _stream = stream;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly string _connString = options.SqlConnectionString;
    private bool _running = true;
    private bool _started = false;

    public event ErrorHandlerEvent? ErrorHandler;

    private async void MonitorResponse()
    {
        if (_started) return;
        _started = true;
        try
        {
            while (_running && await _stream.ResponseStream.MoveNext(_cancellationTokenSource.Token))
            {
                var response = _stream.ResponseStream.Current;
                if (response is not null && _responseHooks.TryGetValue(response.Id, out var hook))
                {
                    await hook.WriteAsync(response);
                }
            }
        }
        catch (Exception ex)
        {
            if (
                ex is not OperationCanceledException
                && ErrorHandler is not null
            ) ErrorHandler(this, ex);
        }
        finally
        {
            _cancellationTokenSource.Cancel();
            _running = false;
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
    }

    public async IAsyncEnumerable<T> Query<T>(string sql)
    where T : class, new()
    {
        if (!_running) throw new InvalidOperationException("Tunnel closed");
        var type = typeof(T);
        var schema = type.GetCachedSchema();

        var results = InternalRun(sql, schema.Item2);
        await foreach (var result in results)
        {
            using var memStream = options.Compress ? result.Result.DecompressData() : result.Result.ToMemoryStream();
            var reader = new BinaryDecoder(memStream);
            var datumReader = new GenericDatumReader<GenericRecord>(schema.Item1, schema.Item1);
            while (memStream.Position < memStream.Length)
            {
                var genericData = datumReader.Read(new GenericRecord(schema.Item1), reader);
                var item = new T();

                schema.Item1.Fields.ForEach(field
                    => type.GetProperty(field.Name)?
                        .SetValue(item, genericData.GetValue(field.Pos))
                );
                yield return item;
            }
        }
    }

    public async ValueTask Execute(string sql)
    {
        await InternalRun(sql, null).LastAsync();
    }

    private async IAsyncEnumerable<SqlResponse> InternalRun(string sql, string? schema)
    {
        var id = GuidEx.NewBase64Guid();
        await _stream.RequestStream.WriteAsync(new()
        {
            Id = id,
            ConnString = _started ? "" : _connString,
            Query = sql,
            Schema = schema ?? "",
            Compress = options.Compress
        });
        MonitorResponse();
        var channel = Channel.CreateUnbounded<SqlResponse>();
        _responseHooks.TryAdd(id, channel.Writer);
        var reader = channel.Reader;

        while (await reader.WaitToReadAsync(_cancellationTokenSource.Token))
        {
            reader.TryRead(out var item);
            if (item is not null)
            {
                if (!string.IsNullOrEmpty(item.Error)) throw new SqlProxyException(item.Error);
                yield return item;
            }
            if (item?.Last == true) break;
        }
        _responseHooks.TryRemove(id, out _);
    }

    public ValueTask<T?> QueryFirstOrDefault<T>(string sql) where T : class, new()
        => Query<T>(sql).FirstOrDefaultAsync();

    public ValueTask<T> QueryFirst<T>(string sql) where T : class, new()
        => Query<T>(sql).FirstAsync();

    public void Dispose()
    {
        _running = false;
        _cancellationTokenSource.Cancel();
    }
}