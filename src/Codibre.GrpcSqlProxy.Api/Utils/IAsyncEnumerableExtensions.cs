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

        public static async IAsyncEnumerable<(ByteString, bool, bool)> EmptyResult(this ValueTask result)
        {
            await result;
            yield return _empty;
        }

        public static async IAsyncEnumerable<(ByteString, bool, bool)> EmptyResult<T>(this ValueTask<T> result)
        {
            await result;
            yield return _empty;
        }

        internal static IAsyncEnumerable<(ByteString, bool, bool)> GetResult(
            this SqlConnection connection,
            SqlRequest request,
            ProxyContext context
        )
        {
            var query = request.Query;
            var options = JsonConvert.DeserializeObject<Dictionary<string, object>?>(request.Params);
            return query.ToUpperInvariant().Replace(";", "") switch
            {
                "BEGIN TRANSACTION" => StartTransaction(connection, context).EmptyResult(),
                "COMMIT" => Commit(context).EmptyResult(),
                "ROLLBACK" => Rollback(context).EmptyResult(),
                _ => string.IsNullOrWhiteSpace(request.Schema)
                                        ? connection.ExecuteAsync(query, options, context.Transaction)
.EmptyResult()
                                        : connection
                                            .QueryUnbufferedAsync(request.Query, options, context.Transaction)
.StreamByteChunks(request.Schema, request.PacketSize, request.Compress),
            };
            ;
        }

        private static async ValueTask Rollback(ProxyContext context)
        {
            await context.Transaction!.RollbackAsync();
            context.Transaction = null;
        }

        private static async ValueTask Commit(ProxyContext context)
        {
            await context.Transaction!.CommitAsync();
            context.Transaction = null;
        }

        private static async ValueTask StartTransaction(SqlConnection connection, ProxyContext context)
        {
            context.Transaction = await connection.BeginTransactionAsync();
        }
    }
}