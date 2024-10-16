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

public sealed class SqlProxyClientTunnel : ISqlProxyClientTunnel
{
    private readonly AsyncDuplexStreamingCall<SqlRequest, SqlResponse> _stream;
    private readonly SqlProxyClientOptions _clientOptions;
    private readonly ConcurrentDictionary<string, ChannelWriter<SqlResponse>> _responseHooks = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly string _connString;
    private bool _running = true;
    private bool _started = false;

    public event ErrorHandlerEvent? ErrorHandler;

    public ISqlProxyBatchQuery Batch { get; }

    internal SqlProxyClientTunnel(
       AsyncDuplexStreamingCall<SqlRequest, SqlResponse> stream,
        SqlProxyClientOptions clientOptions
    )
    {
        Batch = new SqlProxyBatchQuery(this);
        _stream = stream;
        _clientOptions = clientOptions;
        _connString = clientOptions.SqlConnectionString;
    }

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

    public IAsyncEnumerable<T> Query<T>(string sql, SqlProxyQueryOptions? options = null)
    where T : class, new()
    {
        if (!_running) throw new InvalidOperationException("Tunnel closed");
        var type = typeof(T);
        var schema = type.GetCachedSchema();

        var results = InternalRun(sql, [schema.Item2], options);
        return ConvertResult<T>(type, schema, results);
    }

    internal static async IAsyncEnumerable<T> ConvertResult<T>(Type type, (RecordSchema, string) schema, IAsyncEnumerable<SqlResponse> results) where T : class, new()
    {
        await foreach (var result in results)
        {
            using var memStream = result.Compressed ? result.Result.DecompressData() : result.Result.ToMemoryStream();
            var reader = new BinaryDecoder(memStream);
            var datumReader = new GenericDatumReader<GenericRecord>(schema.Item1, schema.Item1);
            while (memStream.Position < memStream.Length)
            {
                var genericData = datumReader.Read(new GenericRecord(schema.Item1), reader);
                var current = new T();

                schema.Item1.Fields.ForEach(field
                    => type.GetProperty(field.Name)?
                        .SetValue(current, genericData.GetValue(field.Pos))
                );
                yield return current;
            }
        }
    }

    public async ValueTask Execute(string sql, SqlProxyQueryOptions? options = null)
    {
        await InternalRun(sql, null, options).LastAsync();
    }

    private async IAsyncEnumerable<SqlResponse> InternalRun(string sql, string[]? schemas, SqlProxyQueryOptions? options)
    {
        var id = GuidEx.NewBase64Guid();
        var message = GetRequest(_clientOptions, sql, schemas, options, id);
        await _stream.RequestStream.WriteAsync(message);
        MonitorResponse();
        var channel = Channel.CreateUnbounded<SqlResponse>();
        _responseHooks.TryAdd(id, channel.Writer);
        var reader = channel.Reader;

        while (await reader.WaitToReadAsync(_cancellationTokenSource.Token))
        {
            if (reader.TryRead(out var current))
            {
                if (!string.IsNullOrEmpty(current.Error)) throw new SqlProxyException(current.Error);
                yield return current;
                if (current.Last == LastEnum.Last) break;
            }
        }
        _responseHooks.TryRemove(id, out _);
    }

    private SqlRequest GetRequest(SqlProxyClientOptions clientOptions, string sql, string[]? schemas, SqlProxyQueryOptions? options, string id)
    {
        SqlRequest message = new()
        {
            Id = id,
            ConnString = _started ? "" : _connString,
            Query = sql,
            Schema = { },
            Compress = options?.Compress ?? clientOptions.Compress,
            PacketSize = options?.PacketSize ?? clientOptions.PacketSize,
            Params = JsonSerializer.Serialize(options?.Params)
        };
        foreach (var schema in schemas ?? [])
        {
            message.Schema.Add(schema);
        }

        return message;
    }

    public ValueTask<T?> QueryFirstOrDefault<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new()
        => Query<T>(sql, options).FirstOrDefaultAsync();

    public ValueTask<T> QueryFirst<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new()
        => Query<T>(sql, options).FirstAsync();

    public void Dispose()
    {
        _running = false;
        _cancellationTokenSource.Cancel();
    }

    public ValueTask BeginTransaction() => Execute("BEGIN TRANSACTION");

    public ValueTask Commit() => Execute("COMMIT");

    public ValueTask Rollback() => Execute("ROLLBACK");

    public Reader QueryMultipleAsync(string sql, string[] schemas, SqlProxyQueryOptions? options)
        => new(InternalRun(sql, schemas, options));
}