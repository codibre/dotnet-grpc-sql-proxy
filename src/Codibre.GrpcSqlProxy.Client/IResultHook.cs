using Codibre.GrpcSqlProxy.Common;

namespace Codibre.GrpcSqlProxy.Client;

public interface IResultHook<T>
{
    T Result { get; }
}