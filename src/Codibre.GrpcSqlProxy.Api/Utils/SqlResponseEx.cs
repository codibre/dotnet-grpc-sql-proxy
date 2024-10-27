using Google.Protobuf;

namespace Codibre.GrpcSqlProxy.Api.Utils;

public static class SqlResponseEx
{
    public static SqlResponse Create(string id, QueryPacket packet) => new()
    {
        Id = id,
        Result = packet.Result,
        Error = "",
        Last = packet.Last,
        Compressed = packet.Compressed,
        Index = packet.Index,
    };

    public static SqlResponse CreateEmpty(string id) => new()
    {
        Id = id,
        Result = ByteString.Empty,
        Error = "",
        Last = LastEnum.Last,
        Compressed = false,
        Index = 0,
    };

    public static SqlResponse CreateError(string id, string error, int index) => new()
    {
        Id = id,
        Result = ByteString.Empty,
        Error = error,
        Last = LastEnum.Last,
        Compressed = false,
        Index = index,
    };
}