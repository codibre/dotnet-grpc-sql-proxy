using Avro;
using AvroSchemaGenerator;

namespace Codibre.GrpcSqlProxy.Common
{
    public static class CachedSchema
    {
        private static readonly Dictionary<string, RecordSchema> _schemas = [];
        private static readonly Dictionary<Type, (RecordSchema, string)> _typeSchemas = [];

        public static RecordSchema GetSchema(string schema)
            => _schemas.GetOrSet(schema, () => (RecordSchema)Schema.Parse(schema));

        public static (RecordSchema, string) GetCachedSchema(this Type type)
            => _typeSchemas.GetOrSet(type, () =>
            {
                var strSchema = type.GetSchema();
                return ((RecordSchema)Schema.Parse(strSchema), strSchema);
            });
    }
}