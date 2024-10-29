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
public class GrpcSqlProxyClientAsyncLocalTest(ITestOutputHelper _testOutputHelper, TestServer _testServer)
{
    [Fact]
    public async Task Should_Keep_Transaction_Opened()
    {
        // Arrange
        var watch = Stopwatch.StartNew();
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        await client.Channel.Execute("DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 400001");
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        await client.Channel.BeginTransaction();
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        await client.Channel.Execute("INSERT INTO TB_PEDIDO (CD_PEDIDO) VALUES (400001)");
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        var result1 = await client.Channel.QueryFirstOrDefault<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 400001");
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        await client.Channel.Rollback();
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        var result2 = await client.Channel.Query<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 400001").ToArrayAsync();
        _testOutputHelper.WriteLine(watch.Elapsed.ToString());
        watch.Stop();

        // Assert
        result1.Should().BeOfType<TB_PEDIDO>();
        result2.Should().BeOfType<TB_PEDIDO[]>();
        result1.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 400001 });
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
        await client.Channel.Execute("DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 500001");
        await client.Channel.BeginTransaction();
        await client.Channel.Execute("INSERT INTO TB_PEDIDO (CD_PEDIDO) VALUES (500001)");
        var result1 = await client.Channel.QueryFirstOrDefault<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 500001");
        await client.Channel.Rollback();
        var result2 = await client.Channel.Query<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 500001").ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PEDIDO>();
        result2.Should().BeOfType<TB_PEDIDO[]>();
        result1.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 500001 });
        result2.Should().BeEquivalentTo(Array.Empty<TB_PEDIDO>());
    }

    [Fact]
    public async Task Should_Use_Compression()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        await client.Channel.BeginTransaction();
        await client.Channel.Execute("DELETE FROM TB_PRODUTO WHERE CD_PRODUTO = 600001");
        await client.Channel.Execute("INSERT INTO TB_PRODUTO (CD_PRODUTO) VALUES (600001)");
        var result1 = await client.Channel.QueryFirstOrDefault<TB_PRODUTO>("SELECT * FROM TB_PRODUTO WHERE CD_PRODUTO = 600001");
        await client.Channel.Rollback();
        var result2 = await client.Channel.Query<TB_PRODUTO>("SELECT * FROM TB_PRODUTO WHERE CD_PRODUTO = 600001").ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PRODUTO>();
        result2.Should().BeOfType<TB_PRODUTO[]>();
        result1.Should().BeEquivalentTo(new TB_PRODUTO { CD_PRODUTO = 600001 });
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
            await client.Channel.Execute("SELECT * FROM INVALID_TABLE");
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
        using var channel2 = client.CreateChannel();
        await client.Channel.Execute("DELETE FROM TB_PESSOA WHERE CD_PESSOA IN (10, 20, 30, 50)");
        await client.Channel.Execute("INSERT INTO TB_PESSOA (CD_PESSOA) VALUES (10)");
        await client.Channel.Execute("INSERT INTO TB_PESSOA (CD_PESSOA) VALUES (20)");
        await client.Channel.BeginTransaction();
        await channel2.BeginTransaction();
        await client.Channel.Execute("UPDATE TB_PESSOA SET CD_PESSOA = 30 WHERE CD_PESSOA = @Id", new()
        {
            Params = new
            {
                Id = 10
            }
        });
        var result1 = await client.Channel.QueryFirst<TB_PESSOA>("SELECT * FROM TB_PESSOA WHERE CD_PESSOA = @Id", new()
        {
            Params = new
            {
                Id = 30
            }
        });
        await client.Channel.Rollback();
        await channel2.Execute("UPDATE TB_PESSOA SET CD_PESSOA = 50 WHERE CD_PESSOA = 20");
        var result2 = await channel2.Query<TB_PESSOA>("SELECT * FROM TB_PESSOA WHERE CD_PESSOA IN (10, 20, 30, 50)").ToArrayAsync();
        await channel2.Rollback();
        var result3 = await client.Channel.Query<TB_PESSOA>("SELECT * FROM TB_PESSOA  WHERE CD_PESSOA IN (10, 20, 30, 50)", new()
        {
            PacketSize = 1
        }).ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PESSOA>();
        result2.Should().BeOfType<TB_PESSOA[]>();
        result3.Should().BeOfType<TB_PESSOA[]>();
        result1.Should().BeEquivalentTo(new TB_PESSOA { CD_PESSOA = 30 });
        result2.OrderBy(x => x.CD_PESSOA).ToArray().Should().BeEquivalentTo(new TB_PESSOA[] {
            new () { CD_PESSOA = 10 },
            new () { CD_PESSOA = 50 }
        });
        result3.OrderBy(x => x.CD_PESSOA).ToArray().Should().BeEquivalentTo(new TB_PESSOA[] {
            new () { CD_PESSOA = 10 },
            new () { CD_PESSOA = 20 }
        });
    }
}