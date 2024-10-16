using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Api.Utils;
using Google.Protobuf;
using Microsoft.Identity.Client;

public record QueryPacket(
    ByteString Result,
    bool Compressed,
    LastEnum Last,
    int Index
)
{
    public static QueryPacket Empty(int index, LastEnum last) => new(ByteString.Empty, false, last, index);
    public static QueryPacket GetMid(ChunkQueue queue, bool compressed, int index)
     => new(queue.Pop(), compressed, LastEnum.Mid, index);

    public static QueryPacket GetSetLast(ChunkQueue queue, bool compressed, int index)
         => new(queue.Pop(), compressed, LastEnum.SetLast, index);

    public static QueryPacket GetLast(ChunkQueue queue, bool compressed, int index, int max)
         => new(queue.Pop(), compressed, LastKind(index, max), index);

    public static LastEnum LastKind(int index, int maxSchema)
    {
        return index < maxSchema ? LastEnum.SetLast : LastEnum.Last;
    }
}