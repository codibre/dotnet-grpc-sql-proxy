using Avro.Generic;
using Codibre.GrpcSqlProxy.Common;
using Dapper;
using Google.Protobuf;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace Codibre.GrpcSqlProxy.Api.Utils
{
    public static class IAsyncEnumerableExtensions
    {
        private static readonly (ByteString, bool, bool) _empty = (ByteString.Empty, true, false);
        public static async IAsyncEnumerable<(ByteString, bool, bool)> StreamByteChunks(this IAsyncEnumerable<dynamic> result, string schema, int packetSize, bool compress)
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
                if (queue.Count > 1) yield return (queue.Pop(), false, compress);
            }
            if (queue.Empty) yield return _empty;
            else
            {
                queue.EnqueueRest();
                while (queue.Count > 1) yield return (queue.Pop(), false, compress);
                if (queue.Count > 0) yield return (queue.Pop(), true, compress);
            }
        }

        public static async IAsyncEnumerable<(ByteString, bool, bool)> EmptyResult(this Task result)
        {
            await result;
            yield return _empty;
        }

        public static IAsyncEnumerable<(ByteString, bool, bool)> GetResult(
            this SqlConnection connection,
            SqlRequest request
        )
        {
            var query = request.Query;
            var options = JsonConvert.DeserializeObject<Dictionary<string, object>?>(request.Params);
            return string.IsNullOrWhiteSpace(request.Schema)
                ? connection.ExecuteAsync(query, options)
                    .EmptyResult()
                : connection
                    .QueryUnbufferedAsync(request.Query, options)
                    .StreamByteChunks(request.Schema, request.PacketSize, request.Compress);
        }
    }
}