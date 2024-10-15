using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client;
using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Codibre.GrpcSqlProxy.Test;

public class GrpcSqlProxyClientTest
{
    [Fact]
    public async Task Should_Keep_Transaction_Opened()
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
        await channel.Execute("DELETE FROM TB_PEDIDO");
        await channel.BeginTransaction();
        await channel.Execute("INSERT INTO TB_PEDIDO (CD_PEDIDO) VALUES (1)");
        var result1 = await channel.QueryFirstOrDefault<TB_PEDIDO>("SELECT * FROM TB_PEDIDO");
        await channel.Rollback();
        var result2 = await channel.Query<TB_PEDIDO>("SELECT * FROM TB_PEDIDO").ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PEDIDO>();
        result2.Should().BeOfType<TB_PEDIDO[]>();
        result1.Should().BeEquivalentTo(new TB_PEDIDO { CD_PEDIDO = 1 });
        result2.Should().BeEquivalentTo(Array.Empty<TB_PEDIDO>());
    }

    [Fact]
    public async Task Should_Use_Compression()
    {
        // Arrange
        var server = await TestServer.Get();
        var client = new GrpcSqlProxyClient(
            new SqlProxyClientOptions(
                server.Url,
                server.Config.GetConnectionString("SqlConnection") ?? throw new Exception("No connection string")
            )
            {
                Compress = true
            }
        );

        // Act
        using var channel = client.CreateChannel();
        await channel.BeginTransaction();
        await channel.Execute("DELETE FROM TB_PRODUTO");
        await channel.Execute("INSERT INTO TB_PRODUTO (CD_PRODUTO) VALUES (1)");
        var result1 = await channel.QueryFirstOrDefault<TB_PRODUTO>("SELECT * FROM TB_PRODUTO");
        await channel.Rollback();
        var result2 = await channel.Query<TB_PRODUTO>("SELECT * FROM TB_PRODUTO").ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PRODUTO>();
        result2.Should().BeOfType<TB_PRODUTO[]>();
        result1.Should().BeEquivalentTo(new TB_PRODUTO { CD_PRODUTO = 1 });
    }

    [Fact]
    public async Task Should_Throw_Error_For_Invalid_Queries()
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
        using var channel1 = client.CreateChannel();
        using var channel2 = client.CreateChannel();
        await channel1.Execute("DELETE FROM TB_PESSOA");
        await channel1.Execute("INSERT INTO TB_PESSOA (CD_PESSOA) VALUES (1)");
        await channel1.Execute("INSERT INTO TB_PESSOA (CD_PESSOA) VALUES (2)");
        await channel1.BeginTransaction();
        await channel2.BeginTransaction();
        await channel1.Execute("UPDATE TB_PESSOA SET CD_PESSOA = 3 WHERE CD_PESSOA = @Id", new()
        {
            Params = new
            {
                Id = 1
            }
        });
        var result1 = await channel1.QueryFirst<TB_PESSOA>("SELECT * FROM TB_PESSOA WHERE CD_PESSOA = @Id", new()
        {
            Params = new
            {
                Id = 3
            }
        });
        await channel1.Rollback();
        await channel2.Execute("UPDATE TB_PESSOA SET CD_PESSOA = 5 WHERE CD_PESSOA = 2");
        var result2 = await channel2.Query<TB_PESSOA>("SELECT * FROM TB_PESSOA").ToArrayAsync();
        await channel2.Rollback();
        var result3 = await channel1.Query<TB_PESSOA>("SELECT * FROM TB_PESSOA", new()
        {
            PacketSize = 1
        }).ToArrayAsync();

        // Assert
        result1.Should().BeOfType<TB_PESSOA>();
        result2.Should().BeOfType<TB_PESSOA[]>();
        result3.Should().BeOfType<TB_PESSOA[]>();
        result1.Should().BeEquivalentTo(new TB_PESSOA { CD_PESSOA = 3 });
        result2.OrderBy(x => x.CD_PESSOA).ToArray().Should().BeEquivalentTo(new TB_PESSOA[] {
            new () { CD_PESSOA = 1 },
            new () { CD_PESSOA = 5 }
        });
        result3.OrderBy(x => x.CD_PESSOA).ToArray().Should().BeEquivalentTo(new TB_PESSOA[] {
            new () { CD_PESSOA = 1 },
            new () { CD_PESSOA = 2 }
        });
    }
}