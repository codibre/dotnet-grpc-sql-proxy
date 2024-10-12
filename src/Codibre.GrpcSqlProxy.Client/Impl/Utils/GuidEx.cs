namespace Codibre.GrpcSqlProxy.Client.Impl.Utils;

public static class GuidEx
{
    public static string NewBase64Guid() => Convert.ToBase64String(Guid.NewGuid().ToByteArray());
}