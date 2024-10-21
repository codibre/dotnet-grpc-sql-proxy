using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using System.Transactions;
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
    private readonly AsyncLocal<ContextInfo?> _context = new();
    private ContextInfo Context
    {
        get
        {
            var result = _context.Value;
            if (result is null || result.Disposed) result = _context.Value = new(_getStream, () => _context.Value = null);
            return result;
        }
    }

    private readonly SqlProxyClientOptions _clientOptions;
    private readonly Func<AsyncDuplexStreamingCall<SqlRequest, SqlResponse>> _getStream;

    public ISqlProxyBatchQuery Batch { get; }

    internal SqlProxyClientTunnel(
        Func<AsyncDuplexStreamingCall<SqlRequest, SqlResponse>> getStream,
        SqlProxyClientOptions clientOptions
    )
    {
        Batch = new SqlProxyBatchQuery(this);
        _getStream = getStream;
        _clientOptions = clientOptions;
    }

    private (ContextInfo, IAsyncEnumerable<T>) QueryInternal<T>(string sql, SqlProxyQueryOptions? options)
    where T : class, new()
    {
        var type = typeof(T);
        var schema = type.GetCachedSchema();

        var (context, results) = InternalRun(sql, [schema.Item2], options);
        return (context, ConvertResult<T>(type, schema, results));
    }

    public IAsyncEnumerable<T> Query<T>(string sql, SqlProxyQueryOptions? options = null)
    where T : class, new()
        => QueryInternal<T>(sql, options).Item2;

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

    public ValueTask Execute(
        string sql,
        SqlProxyQueryOptions? options = null
    ) => InternalRun(sql, null, options).Item2.Complete();

    private (ContextInfo, IAsyncEnumerable<SqlResponse>) InternalRun(
        string sql,
        string[]? schemas,
        SqlProxyQueryOptions? options
    )
    {
        var context = Context;
        return (context, InternalRun(sql, schemas, options, context));
    }

    private async IAsyncEnumerable<SqlResponse> InternalRun(
        string sql,
        string[]? schemas,
        SqlProxyQueryOptions? options,
        ContextInfo context
    )
    {
        var id = GuidEx.NewBase64Guid();
        var message = GetRequest(
            _clientOptions,
            sql,
            schemas,
            options,
            id,
            context
        );
        await context.Stream.RequestStream.WriteAsync(message);
        context.Monitor.Start();
        var channel = Channel.CreateUnbounded<SqlResponse>();
        context.Monitor.AddHook(id, channel.Writer);
        var reader = channel.Reader;

        while (await reader.WaitToReadAsync(context.Monitor.CancellationToken))
        {
            if (!reader.TryRead(out var current)) continue;
            if (!string.IsNullOrEmpty(current.Error)) throw new SqlProxyException(current.Error);
            yield return current;
            if (current.Last == LastEnum.Last) break;
        }
        ClearWhenNotInTransaction(context);
    }

    private static void ClearWhenNotInTransaction(ContextInfo context)
    {
        if (!context.Transaction) context.Dispose();
    }

    private SqlRequest GetRequest(
        SqlProxyClientOptions clientOptions,
        string sql, string[]? schemas,
        SqlProxyQueryOptions? options,
        string id,
        ContextInfo context
    )
    {
        SqlRequest message = new()
        {
            Id = id,
            ConnString = context.Monitor.Started ? "" : _clientOptions.SqlConnectionString,
            Query = sql,
            Schema = { },
            Compress = options?.Compress ?? clientOptions.Compress,
            PacketSize = options?.PacketSize ?? clientOptions.PacketSize,
            Params = JsonSerializer.Serialize(options?.Params)
        };
        foreach (var schema in schemas ?? []) message.Schema.Add(schema);

        return message;
    }

    public async ValueTask<T?> QueryFirstOrDefault<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new()
    {
        var (contextInfo, results) = QueryInternal<T>(sql, options);
        var result = await results.FirstOrDefaultAsync();
        ClearWhenNotInTransaction(contextInfo);
        return result;
    }

    public async ValueTask<T> QueryFirst<T>(string sql, SqlProxyQueryOptions? options = null) where T : class, new()
    {
        var (contextInfo, results) = QueryInternal<T>(sql, options);
        var result = await results.FirstAsync();
        ClearWhenNotInTransaction(contextInfo);
        return result;
    }

    public void Dispose()
    {
        _context.Value?.Dispose();
        _context.Value = null;
    }

    public ValueTask BeginTransaction()
    {
        Context.Transaction = true;
        return Execute("BEGIN TRANSACTION");
    }

    public ValueTask Commit()
    {
        Context.Transaction = false;
        return Execute("COMMIT");
    }

    public ValueTask Rollback()
    {
        Context.Transaction = false;
        return Execute("ROLLBACK");
    }

    public Reader QueryMultipleAsync(string sql, string[] schemas, SqlProxyQueryOptions? options)
        => new(InternalRun(sql, schemas, options).Item2);

    public void OnError(ErrorHandlerEvent handler)
    {
        var monitor = _context.Value?.Monitor;
        if (monitor is not null) monitor.ErrorHandler += handler;
    }

    public void Start() => _ = Context;
}