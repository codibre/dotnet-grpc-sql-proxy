using System.Security.Cryptography;
using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client;
using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Client;
using Xunit.Abstractions;

namespace Codibre.GrpcSqlProxy.Test;

public sealed class TestServer : IDisposable
{
    private WebApplication? _app = null;
    private Task? _start = null;
    private ValueTask? _noop = null;
    private GrpcSqlProxyClient? _client = null;

    private string? _port;

    private string GetPort()
    {
        _port = RandomNumberGenerator.GetInt32(3000, 4000).ToString();
        return _port;
    }

    public string Url => $"http://localhost:{_port}";
    public IConfigurationRoot Config { get; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddEnvironmentVariables()
            .Build();

    public async Task<GrpcSqlProxyClient> GetClient(ITestOutputHelper _testOutputHelper)
    {
        if (_app is null || _start is null)
        {
            _start = StartRun(_testOutputHelper);
        }
        await _start;
        if (_client is null || _noop is null)
        {
            _client = new GrpcSqlProxyClient(
                            new SqlProxyClientOptions(
                                Url,
                                Config.GetConnectionString("SqlConnection") ?? throw new Exception("No connection string")
                            )
                            {
                                Compress = false
                            }
                        );
            _noop = _client.Initialize();
        }
        await _noop.Value;
        return _client;
    }

    private async Task StartRun(ITestOutputHelper logger)
    {
        logger.WriteLine("Starting server");
        _app = Program.GetApp([GetPort()]);
        await _app.StartAsync();
        logger.WriteLine("Server started");
    }

    public void Dispose()
    {
        _client?.Channel.Dispose();
        _app?.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }
}