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

public class TestServer : IDisposable
{
    private readonly WebApplication _app;
    private static Task _run;
    private static TestServer? _instance = null;

    public string Url { get; } = "http://localhost:3000";
    public IConfigurationRoot Config { get; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddEnvironmentVariables()
            .Build();

    private TestServer()
    {
        _app = Program.GetApp([]);
        _run ??= StartApp(_app);
    }

    private async Task StartApp(WebApplication app)
    {
        _ = Task.Run(() => app.RunAsync());
        await Task.Delay(1000);
    }

    public static async Task<TestServer> Get()
    {
        _instance ??= new TestServer();
        await _run;
        return _instance;
    }

    public void Dispose()
    {
        _app.StopAsync().GetAwaiter().GetResult();
    }
}