using System.Security.Cryptography;
using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client;
using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace Codibre.GrpcSqlProxy.Test;

public class TB_PRODUTO
{
    public int CD_PRODUTO { get; set; }
}
public class TB_PEDIDO
{
    public int CD_PEDIDO { get; set; }
}

public class TB_PESSOA
{
    public int CD_PESSOA { get; set; }
}

public class TestServer
{
    private readonly WebApplication _app;
    private static Task? _run = null;
    private static TestServer? _instance = null;

    private static readonly string _port = RandomNumberGenerator.GetInt32(3000, 4000).ToString();
    public string Url { get; } = $"http://localhost:{_port}";
    public IConfigurationRoot Config { get; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddEnvironmentVariables()
            .Build();

    private TestServer()
    {
        _app = Program.GetApp([_port]);
        _run ??= StartApp(_app);
    }

    private async Task StartApp(WebApplication app)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await app.RunAsync();
            }
            catch
            {
                // Ignore
            }
        });
        await Task.Delay(1000);
    }

    public static async Task<TestServer> Get()
    {
        _instance ??= new TestServer();
        if (_run is not null) await _run;
        return _instance;
    }
}