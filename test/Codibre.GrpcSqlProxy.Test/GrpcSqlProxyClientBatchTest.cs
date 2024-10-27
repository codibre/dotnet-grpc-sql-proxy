using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client;
using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Codibre.GrpcSqlProxy.Test;

[Collection("TestServerCollection")]
public class GrpcSqlProxyClientBatchTest(ITestOutputHelper _testOutputHelper, TestServer _testServer)
{
    private IEnumerable<int> GetList()
    {
        for (var i = 0; i < 3000; i++) yield return i;
    }

    [Fact]
    public async Task Should_Run_Transaction_In_Batch()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel = client.CreateChannel();
        await channel.Batch.RunInTransaction(() =>
        {
            channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 123456");
            channel.Batch.AddNoResultScript($"INSERT INTO TB_PEDIDO VALUES (123456)");
            channel.Batch.CancelTransaction();
        });
        var result = await channel.Query<TB_PEDIDO>("SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 123456").ToArrayAsync();

        // Assert
        result.Should().BeOfType<TB_PEDIDO[]>();
        result.Should().BeEquivalentTo(Array.Empty<TB_PEDIDO>());
    }

    [Fact]
    public async Task Should_Run_Transaction_In_One_RoundTrip()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel = client.CreateChannel();
        await channel.Batch.RunInTransaction(() =>
        {
            channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 12345");
            channel.Batch.AddNoResultScript($"INSERT INTO TB_PEDIDO VALUES (12345)");
        });
        var resultHook = channel.Batch.QueryHook<TB_PEDIDO>($"SELECT * FROM TB_PEDIDO WHERE CD_PEDIDO = 12345");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 12345");
        await channel.Batch.RunQueries();
        var result = resultHook.Result;

        // Assert
        result.Should().BeOfType<TB_PEDIDO[]>();
        result.Should().BeEquivalentTo(new TB_PEDIDO[]
        {
            new () { CD_PEDIDO = 12345 }
        });
    }

    [Fact]
    public async Task Should_Run_Query_Batch()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel = client.CreateChannel();
        channel.Batch.AddNoResultScript($"BEGIN TRANSACTION");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 50001");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PRODUTO WHERE CD_PRODUTO = 50002");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PESSOA WHERE CD_PESSOA IN (50003, 50004)");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PEDIDO VALUES ({50001})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PRODUTO VALUES ({50002})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({50003})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({50004})");
        var orderHook = channel.Batch.QueryFirstHook<TB_PEDIDO>($"SELECT TOP 1 * FROM TB_PEDIDO WHERE CD_PEDIDO = 50001");
        var personHook = channel.Batch.QueryHook<TB_PESSOA>($"SELECT * FROM TB_PESSOA WHERE CD_PESSOA IN (50003, 50004)");
        var productHook = channel.Batch.QueryFirstOrDefaultHook<TB_PRODUTO>($"SELECT TOP 1 * FROM TB_PRODUTO WHERE CD_PRODUTO = 50002");
        channel.Batch.AddNoResultScript($"ROLLBACK");
        await channel.Batch.RunQueries();

        // Assert
        orderHook.Result.Should().BeOfType<TB_PEDIDO>();
        personHook.Result.ToArray().Should().BeOfType<TB_PESSOA[]>();
        productHook.Result.Should().BeOfType<TB_PRODUTO>();
        orderHook.Result.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 50001 });
        personHook.Result.Should().BeEquivalentTo([
            new TB_PESSOA { CD_PESSOA = 50003 },
            new TB_PESSOA { CD_PESSOA = 50004 }
        ]);
        productHook.Result.Should().BeEquivalentTo(new TB_PRODUTO { CD_PRODUTO = 50002 });
    }

    [Fact]
    public async Task Should_Run_Query_Batch_Using_CustomOptions()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel = client.CreateChannel();
        channel.Batch.AddNoResultScript($"BEGIN TRANSACTION");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO WHERE CD_PEDIDO = 40001");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PRODUTO WHERE CD_PRODUTO = 40002");
        channel.Batch.AddNoResultScript($"DELETE FROM TB_PESSOA WHERE CD_PESSOA IN (40003, 40004)");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PEDIDO VALUES ({40001})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PRODUTO VALUES ({40002})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({40003})");
        channel.Batch.AddNoResultScript($"INSERT INTO TB_PESSOA VALUES ({40004})");
        var orderHook = channel.Batch.QueryFirstHook<TB_PEDIDO>($"SELECT TOP 1 * FROM TB_PEDIDO WHERE CD_PEDIDO = 40001");
        var personHook = channel.Batch.QueryHook<TB_PESSOA>($"SELECT * FROM TB_PESSOA WHERE CD_PESSOA IN (40003, 40004)");
        var productHook = channel.Batch.QueryFirstOrDefaultHook<TB_PRODUTO>($"SELECT TOP 1 * FROM TB_PRODUTO WHERE CD_PRODUTO = 40002");
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
        orderHook.Result.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 40001 });
        personHook.Result.Should().BeEquivalentTo([
            new TB_PESSOA { CD_PESSOA = 40003 },
            new TB_PESSOA { CD_PESSOA = 40004 }
        ]);
        productHook.Result.Should().BeEquivalentTo(new TB_PRODUTO { CD_PRODUTO = 40002 });
    }

    [Fact]
    public async Task Should_Deal_With_Parameter_Limitation()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel = client.CreateChannel();
        List<(int, TB_PEDIDO)> list = [];
        var pars = GetList().Select(x => x * 10000).ToArray();
        await channel.Batch.RunInTransaction(async () =>
        {
            channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO WHERE CD_PEDIDO >= {pars.Min()} AND CD_PEDIDO <= {pars.Max()}");
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

    [Fact]
    public async Task Should_Run_PrepareBatchQuery_WithAsyncCallback()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);

        // Act
        using var channel = client.CreateChannel();
        List<(int, TB_PEDIDO)> list = [];
        var pars = GetList().Select(x => x * 1000).ToArray();
        await channel.Batch.RunInTransaction(async () =>
        {
            channel.Batch.AddNoResultScript($"DELETE FROM TB_PEDIDO WHERE CD_PEDIDO >= {pars.Min()} AND CD_PEDIDO <= {pars.Max()}");
            foreach (var i in pars)
            {
                await channel.Batch.AddTransactionScript($"INSERT INTO TB_PEDIDO VALUES ({i})");
            }
            await channel.Batch.FlushTransaction();
            await pars.PrepareQueryBatch(channel.Batch, (i, b) =>
            {
                return new ValueTask<IResultHook<TB_PEDIDO>>(b.QueryFirstHook<TB_PEDIDO>(@$"SELECT *
                    FROM TB_PEDIDO
                    WHERE CD_PEDIDO = {i}"));
            })
            .ForEachAwaitAsync(x =>
            {
                list.Add((x.Key, x.Value.Result));
                return Task.CompletedTask;
            });
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