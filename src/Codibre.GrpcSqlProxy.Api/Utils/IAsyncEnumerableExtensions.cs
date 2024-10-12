using Avro.Generic;
using Dapper;
using Google.Protobuf;
using Codibre.GrpcSqlProxy.Common;
using Microsoft.Data.SqlClient;

namespace Codibre.GrpcSqlProxy.Api.Utils;

public static class IAsyncEnumerableExtensions
{
    private static readonly (ByteString, bool) _empty = (ByteString.Empty, true);
    public static async IAsyncEnumerable<(ByteString, bool)> StreamByteChunks(this IAsyncEnumerable<dynamic> result, string schema, int packetSize, bool compress)
    {
        var schemaResult = CachedSchema.GetSchema(schema);
        var queue = new ChunkQueue(compress, schemaResult, packetSize);
        await foreach (var item in result)
        {
            var dict = (IDictionary<string, object>)item;
            var record = new GenericRecord(schemaResult);
            foreach (var field in schemaResult.Fields.Select(x => x.Name))
            {
                if (dict.TryGetValue(field, out var value)) record.Add(field, value);
            }
            queue.Write(record);
            if (queue.Count > 1) yield return (queue.Pop(), false);
        }
        if (queue.Empty) yield return _empty;
        else {
            queue.EnqueueRest();
            while (queue.Count > 1) yield return (queue.Pop(), false);
            if (queue.Count > 0) yield return (queue.Pop(), true);
        }
    }

    public static async IAsyncEnumerable<(ByteString, bool)> EmptyResult(this Task result)
    {
        await result;
        yield return _empty;
    }

    public static IAsyncEnumerable<(ByteString, bool)> GetResult(
        this SqlConnection connection,
        SqlRequest request
    )
    {
        var query = request.Query;
        return string.IsNullOrWhiteSpace(request.Schema)
            ? connection.ExecuteAsync(query)
                .EmptyResult()
            : connection
                .QueryUnbufferedAsync(request.Query)
                .StreamByteChunks(request.Schema, request.PacketSize, request.Compress);
    }
}