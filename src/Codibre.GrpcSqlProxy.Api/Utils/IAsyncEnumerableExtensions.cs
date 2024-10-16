using Avro.Generic;
using Codibre.GrpcSqlProxy.Common;
using Dapper;
using Google.Protobuf;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;

namespace Codibre.GrpcSqlProxy.Api.Utils;

public static class IAsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<QueryPacket> StreamByteChunks(
        this IAsyncEnumerable<object> result,
        string schema,
        int packetSize,
        bool compress,
        int index,
        int maxSchema
    )
    {
        var schemaResult = CachedSchema.GetSchema(schema);
        var queue = new ChunkQueue(compress, schemaResult, packetSize);
        await foreach (var current in result)
        {
            var dict = (IDictionary<string, object>)current;
            var record = new GenericRecord(schemaResult);
            foreach (var field in schemaResult.Fields.Select(x => x.Name))
            {
                if (dict.TryGetValue(field, out var value)) record.Add(field, value);
            }
            queue.Write(record);
            if (queue.Count > 1) yield return QueryPacket.GetMid(queue, compress, index);
        }
        if (queue.Empty) yield return QueryPacket.Empty(index, QueryPacket.LastKind(index, maxSchema));
        else
        {
            queue.EnqueueRest();
            while (queue.Count > 1) yield return QueryPacket.GetMid(queue, compress, index);
            if (queue.Count > 0) yield return QueryPacket.GetLast(queue, compress, index, maxSchema);
        }
    }

    public static IAsyncEnumerable<QueryPacket> EmptyResult(this Task result, int index, LastEnum last)
        => EmptyResult(new ValueTask(result), index, last);

    public static async IAsyncEnumerable<QueryPacket> EmptyResult(this ValueTask result, int index, LastEnum last)
    {
        await result;
        yield return QueryPacket.Empty(index, last);
    }

    internal static IAsyncEnumerable<QueryPacket> GetResult(
        this ProxyContext context,
        SqlRequest request
    )
    {
        var query = request.Query;
        var options = JsonConvert.DeserializeObject<Dictionary<string, object>?>(request.Params);
        if (request.Schema.Count > 1) return RunMultipleQuery(request, context, options);
        return query.ToUpperInvariant().Replace(";", "") switch
        {
            "BEGIN TRANSACTION" => StartTransaction(context).EmptyResult(0, LastEnum.Last),
            "COMMIT" => context.Commit().EmptyResult(0, LastEnum.Last),
            "ROLLBACK" => context.Rollback().EmptyResult(0, LastEnum.Last),
            _ => RunQuery(request, context, options, request.Schema.FirstOrDefault() ?? "", 0, 0)
        };
    }

    private static IAsyncEnumerable<QueryPacket> RunMultipleQuery(
        SqlRequest request,
        ProxyContext context,
        Dictionary<string, object>? options
    )
    {
        var reader = QueryMultiple(request, context, options);
        var max = request.Schema.Count - 1;
        return reader
            .Select((resultSet, pos) =>
            {
                context.Index = pos;
                return new { resultSet, pos, schema = request.Schema[pos] };
            })
            .SelectMany(x => x.resultSet
                .StreamByteChunks(x.schema, request.PacketSize, request.Compress, x.pos, max)
            );
    }

    private static async IAsyncEnumerable<IAsyncEnumerable<object>> QueryMultiple(SqlRequest request, ProxyContext context, Dictionary<string, object>? options)
    {
        var reader = await context.QueryMultiple(request.Query, options);

        while (!reader.IsConsumed) yield return reader.ReadUnbufferedAsync();
    }

    private static IAsyncEnumerable<QueryPacket> RunQuery(
        SqlRequest request,
        ProxyContext context,
        Dictionary<string, object>? options,
        string schema,
        int pos,
        int max
    ) => string.IsNullOrWhiteSpace(schema)
        ? context.Execute(request.Query, options)
            .EmptyResult(pos, QueryPacket.LastKind(pos, max))
        : context
            .Query(request.Query, options)
            .StreamByteChunks(schema, request.PacketSize, request.Compress, pos, max);

    private static async ValueTask StartTransaction(ProxyContext context)
        => await context.StartTransaction();
}