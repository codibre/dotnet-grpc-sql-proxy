using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client;
using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Codibre.GrpcSqlProxy.Test;

[Collection("Sequential")]
public class GrpcSqlProxyClientBatchTest
{
    [Fact]
    public async Task Should_Run_Transaction_In_Batch()
    {
        // Arrange
        var server = await TestServer.Get();
        var client = new GrpcSqlProxyClient(
            new SqlProxyClientOptions(
                server.Url,
                server.Config.GetConnectionString("SqlConnection") ?? throw new Exception("No connection string")
            )
            {
                Compress = false
            }
        );

        // Act
        using var channel = client.CreateChannel();
        await channel.Batch.RunInTransaction(() =>
        {
            channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO");
            channel.Batch.AddNoResultScript($"INSERT INTO TB_PEDIDO VALUES (12345)");
            channel.Batch.CancelTransaction();
        });
        var result = await channel.Query<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 12345").ToArrayAsync();

        // Assert
        result.Should().BeOfType<TB_PEDIDO[]>();
        result.Should().BeEquivalentTo(Array.Empty<TB_PEDIDO>());
    }

    [Fact]
    public async Task Should_Run_Query_Batch()
    {
        // Arrange
        var server = await TestServer.Get();
        var client = new GrpcSqlProxyClient(
            new SqlProxyClientOptions(
                server.Url,
                server.Config.GetConnectionString("SqlConnection") ?? throw new Exception("No connection string")
            )
            {
                Compress = false
            }
        );

        // Act
        using var channel = client.CreateChannel();
        channel.Batch.AddNoResultScript($"BEGIN TRANSACTION");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PRODUTO");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PESSOA");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PEDIDO VALUES ({1})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PRODUTO VALUES ({2})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({3})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({4})");
        var orderHook = channel.Batch.QueryFirstHook<TB_PEDIDO>($"SELECT TOP 1 * FROM TB_PEDIDO");
        var personHook = channel.Batch.QueryHook<TB_PESSOA>($"SELECT * FROM TB_PESSOA");
        var productHook = channel.Batch.QueryFirstOrDefaultHook<TB_PRODUTO>($"SELECT TOP 1 * FROM TB_PRODUTO");
        channel.Batch.AddNoResultScript($"ROLLBACK");
        await channel.Batch.RunQueries();

        // Assert
        orderHook.Result.Should().BeOfType<TB_PEDIDO>();
        personHook.Result.ToArray().Should().BeOfType<TB_PESSOA[]>();
        productHook.Result.Should().BeOfType<TB_PRODUTO>();
        orderHook.Result.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 1 });
        personHook.Result.Should().BeEquivalentTo([
            new TB_PESSOA { CD_PESSOA = 3 },
            new TB_PESSOA { CD_PESSOA = 4 }
        ]);
        productHook.Result.Should().BeEquivalentTo(new TB_PRODUTO { CD_PRODUTO = 2 });
    }

    [Fact]
    public async Task Should_Run_Query_Batch_Using_CustomOptions()
    {
        // Arrange
        var server = await TestServer.Get();
        var client = new GrpcSqlProxyClient(
            new SqlProxyClientOptions(
                server.Url,
                server.Config.GetConnectionString("SqlConnection") ?? throw new Exception("No connection string")
            )
            {
                Compress = false
            }
        );

        // Act
        using var channel = client.CreateChannel();
        channel.Batch.AddNoResultScript($"BEGIN TRANSACTION");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PRODUTO");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PESSOA");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PEDIDO VALUES ({1})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PRODUTO VALUES ({2})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({3})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({4})");
        var orderHook = channel.Batch.QueryFirstHook<TB_PEDIDO>($"SELECT TOP 1 * FROM TB_PEDIDO");
        var personHook = channel.Batch.QueryHook<TB_PESSOA>($"SELECT * FROM TB_PESSOA");
        var productHook = channel.Batch.QueryFirstOrDefaultHook<TB_PRODUTO>($"SELECT TOP 1 * FROM TB_PRODUTO");
        channel.Batch.AddNoResultScript($"ROLLBACK");
        await channel.Batch.RunQueries(new()
        {
            Compress = true,
            PacketSize = 1
        });

        // Assert
        orderHook.Result.Should().BeOfType<TB_PEDIDO>();
        personHook.Result.ToArray().Should().BeOfType<TB_PESSOA[]>();
        productHook.Result.Should().BeOfType<TB_PRODUTO>();
        orderHook.Result.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 1 });
        personHook.Result.Should().BeEquivalentTo([
            new TB_PESSOA { CD_PESSOA = 3 },
            new TB_PESSOA { CD_PESSOA = 4 }
        ]);
        productHook.Result.Should().BeEquivalentTo(new TB_PRODUTO { CD_PRODUTO = 2 });
    }

    private IEnumerable<int> GetList()
    {
        for (var i = 0; i < 3000; i++) yield return i;
    }

    [Fact]
    public async Task Should_Deal_With_Parameter_Limitation()
    {
        // Arrange
        var server = await TestServer.Get();
        var client = new GrpcSqlProxyClient(
            new SqlProxyClientOptions(
                server.Url,
                server.Config.GetConnectionString("SqlConnection") ?? throw new Exception("No connection string")
            )
            {
                Compress = false
            }
        );

        // Act
        using var channel = client.CreateChannel();
        List<(int, TB_PEDIDO)> list = [];
        var pars = GetList().ToArray();
        await channel.Batch.RunInTransaction(async () =>
        {
            channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO");
            foreach (var i in pars)
            {
                await channel.Batch.AddTransactionScript($"INSERT INTO TB_PEDIDO VALUES ({i})");
            }
            await channel.Batch.FlushTransaction();
            await pars.PrepareQueryBatch(channel.Batch, (i, b) =>
            {
                return b.QueryFirstHook<TB_PEDIDO>(@$"SELECT *
                    FROM TB_PEDIDO
                    WHERE CD_PEDIDO = {i}");
            })
            .ForEachAsync(x => list.Add((x.Key, x.Value.Result)));
            channel.Batch.CancelTransaction();
        });

        // Assert
        list.Count.Should().Be(pars.Length);
        list.Should().BeEquivalentTo(pars.Select(
            x => (x, new TB_PEDIDO
            {
                CD_PEDIDO = x
            })
        ));
    }
}