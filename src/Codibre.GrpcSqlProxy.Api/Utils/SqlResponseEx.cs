using Google.Protobuf;

namespace Codibre.GrpcSqlProxy.Api.Utils;

public static class SqlResponseEx
{
    public static SqlResponse Create(string id, ByteString result, bool last) => new()
    {
        Id = id,
        Result = result,
        Error = "",
        Last = last
    };

    public static SqlResponse CreateError(string id, string error) => new()
    {
        Id = id,
        Result = ByteString.Empty,
        Error = error,
        Last = true
    };
}