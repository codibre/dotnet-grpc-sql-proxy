using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.Extensions.DependencyInjection;

namespace Codibre.GrpcSqlProxy.Client;

public static class SqlProxyClientExtensions
{
    public static IServiceCollection AddGrpcSqlProxy(this IServiceCollection services)
        => services
            .AddSingleton<SqlProxyClientOptions>()
            .AddSingleton<ISqlProxyClient, GrpcSqlProxyClient>();
}