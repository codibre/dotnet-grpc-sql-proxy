﻿using Codibre.GrpcSqlProxy.Api.Services;

namespace Codibre.GrpcSqlProxy.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var app = GetApp(args);
        Console.WriteLine($"Listening to {string.Join(",", app.Urls)}");
        await app.RunAsync();
    }

    public static WebApplication GetApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration
            .GetSection("Kestrel")
            .GetSection("EndpointDefaults")
            .GetSection("Protocols").Value = "Http2";

        // Add services to the container.
        builder.Services.AddGrpc();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.MapGrpcService<SqlProxyService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
        var port = args.FirstOrDefault() ?? app.Configuration.GetSection("PORT").Value ?? "3000";
        app.Urls.Add($"http://0.0.0.0:{port}");
        return app;
    }
}