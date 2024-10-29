using System.Diagnostics;
using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client;
using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace Codibre.GrpcSqlProxy.Test;

[Collection("TestServerCollection")]
public class GrpcSqlProxyClientTest(ITestOutputHelper _testOutputHelper, TestServer _testServer)
{
    [Fact]
    public async Task Should_Keep_Transaction_Opened()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        await Task.WhenAll(
            RunTest(client),
            RunTest(client),
            RunTest(client)
        );
    }

    private async Task RunTest(GrpcSqlProxyClient client)
    {
        var watch = Stopwatch.StartNew();
        _testOutputHelper.WriteLine($"First {watch.Elapsed}");
        using var channel = client.CreateChannel();
        _testOutputHelper.WriteLine($"Create {watch.Elapsed}");
        await channel.Noop();
        _testOutputHelper.WriteLine($"Noop {watch.Elapsed}");
        await channel.Connect();
        _testOutputHelper.WriteLine($"Connect {watch.Elapsed}");
        await channel.Execute("DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 600001");
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        await channel.BeginTransaction();
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        await channel.Execute("INSERT INTO TB_PEDIDO (CD_PEDIDO) VALUES (600001)");
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        var result1 = await channel.QueryFirstOrDefault<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 600001");
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        await channel.Rollback();
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        var result2 = await channel.Query<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 600001").ToArrayAsync();
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());

        // Assert
        result1.Should().BeOfType<TB_PEDIDO>();
        result2.Should().BeOfType<TB_PEDIDO[]>();
        result1.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 600001 });
        result2.Should().BeEquivalentTo(Array.Empty<TB_PEDIDO>());
    }

    [Fact]
    public async Task Should_Inject_SqlProxy_Properly()
    {
        // Arrange
        await _testServer.GetClient(_testOutputHelper);
        var builder = Host.CreateApplicationBuilder([]);
        builder.Configuration.GetSection("GrpcSqlProxy").GetSection("Url").Value = _testServer.Url;
        builder.Configuration.GetSection("GrpcSqlProxy").GetSection("Compress").Value = "False";
        builder.Configuration.GetSection("GrpcSqlProxy").GetSection("PacketSize").Value = "2000";
        builder.Services.AddGrpcSqlProxy();
        var app = builder.Build();
        var client = app.Services.GetRequiredService<ISqlProxyClient>();

        // Act
        using var channel = client.CreateChannel();
        await channel.Execute("DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 700001");
        await channel.BeginTransaction();
        await channel.Execute("INSERT INTO TB_PEDIDO (CD_PEDIDO) VALUES (700001)");
        var result1 = await channel.QueryFirstOrDefault<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 700001");
        await channel.Rollback();
        var result2 = await channel.Query<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 700001").ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PEDIDO>();
        result2.Should().BeOfType<TB_PEDIDO[]>();
        result1.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 700001 });
        result2.Should().BeEquivalentTo(Array.Empty<TB_PEDIDO>());
    }

    [Fact]
    public async Task Should_Use_Compression()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel = client.CreateChannel();
        await channel.BeginTransaction();
        await channel.Execute("DELETE FROM TB_PRODUTO WHERE CD_PRODUTO = 800001");
        await channel.Execute("INSERT INTO TB_PRODUTO (CD_PRODUTO) VALUES (800001)");
        var result1 = await channel.QueryFirstOrDefault<TB_PRODUTO>("SELECT * FROM TB_PRODUTO WHERE CD_PRODUTO = 800001");
        await channel.Rollback();
        var result2 = await channel.Query<TB_PRODUTO>("SELECT * FROM TB_PRODUTO WHERE CD_PRODUTO = 800001").ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PRODUTO>();
        result2.Should().BeOfType<TB_PRODUTO[]>();
        result1.Should().BeEquivalentTo(new TB_PRODUTO { CD_PRODUTO = 800001 });
    }

    [Fact]
    public async Task Should_Throw_Error_For_Invalid_Queries()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);
        Exception? thrownException = null;

        // Act
        try
        {
            using var channel = client.CreateChannel();
            await channel.Execute("SELECT * FROM INVALID_TABLE");
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        // Assert
        thrownException.Should().BeOfType<SqlProxyException>();
    }

    [Fact]
    public async Task Should_Keep_Parallel_Transaction_Opened()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel1 = client.CreateChannel();
        using var channel2 = client.CreateChannel();
        await channel1.Execute("DELETE FROM TB_PESSOA WHERE CD_PESSOA IN (100, 200, 300, 500)");
        await channel1.Execute("INSERT INTO TB_PESSOA (CD_PESSOA) VALUES (100)");
        await channel1.Execute("INSERT INTO TB_PESSOA (CD_PESSOA) VALUES (200)");
        await channel1.BeginTransaction();
        await channel2.BeginTransaction();
        await channel1.Execute("UPDATE TB_PESSOA SET CD_PESSOA = 300 WHERE CD_PESSOA = @Id", new()
        {
            Params = new
            {
                Id = 100
            }
        });
        var result1 = await channel1.QueryFirst<TB_PESSOA>("SELECT * FROM TB_PESSOA WHERE CD_PESSOA = @Id", new()
        {
            Params = new
            {
                Id = 300
            }
        });
        await channel1.Rollback();
        await channel2.Execute("UPDATE TB_PESSOA SET CD_PESSOA = 500 WHERE CD_PESSOA = 200");
        var result2 = await channel2.Query<TB_PESSOA>("SELECT * FROM TB_PESSOA WHERE CD_PESSOA IN (100, 200, 300, 500)").ToArrayAsync();
        await channel2.Rollback();
        var result3 = await channel1.Query<TB_PESSOA>("SELECT * FROM TB_PESSOA WHERE CD_PESSOA IN (100, 200, 300, 500)", new()
        {
            PacketSize = 1
        }).ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PESSOA>();
        result2.Should().BeOfType<TB_PESSOA[]>();
        result3.Should().BeOfType<TB_PESSOA[]>();
        result1.Should().BeEquivalentTo(new TB_PESSOA { CD_PESSOA = 300 });
        result2.OrderBy(x => x.CD_PESSOA).ToArray().Should().BeEquivalentTo(new TB_PESSOA[] {
            new () { CD_PESSOA = 100 },
            new () { CD_PESSOA = 500 }
        });
        result3.OrderBy(x => x.CD_PESSOA).ToArray().Should().BeEquivalentTo(new TB_PESSOA[] {
            new () { CD_PESSOA = 100 },
            new () { CD_PESSOA = 200 }
        });
    }
}