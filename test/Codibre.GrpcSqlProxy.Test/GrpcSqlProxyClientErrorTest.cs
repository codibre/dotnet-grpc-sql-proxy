using Codibre.GrpcSqlProxy.Api;
using Codibre.GrpcSqlProxy.Client;
using Codibre.GrpcSqlProxy.Client.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Codibre.GrpcSqlProxy.Test;

[Collection("TestServerCollection")]
public class GrpcSqlProxyClientErrorTest(ITestOutputHelper _testOutputHelper, TestServer _testServer)
{
    [Fact]
    public async Task Should_Throw_An_Error_For_InvalidTable()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);
        Exception? thrownError = null;

        // Act
        try
        {
            using var channel = client.CreateChannel();
            await channel.Execute("DELETE FROM NonExistingTable");
        }
        catch (Exception err)
        {
            thrownError = err;
        }

        // Assert
        thrownError.Should().BeOfType<SqlProxyException>();
    }

    [Fact]
    public async Task Should_Throw_An_Error_For_InvalidSyntax()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);
        Exception? thrownError = null;

        // Act
        try
        {
            using var channel = client.CreateChannel();
            await channel.Execute("DELETE FRO TB_PRODUTO");
        }
        catch (Exception err)
        {
            thrownError = err;
        }

        // Assert
        thrownError.Should().BeOfType<SqlProxyException>();
    }
    [Fact]
    public async Task Should_Throw_An_Error_For_InvalidTable_OnBatch()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);
        Exception? thrownError = null;

        // Act
        try
        {
            using var channel = client.CreateChannel();
            channel.Batch.AddNoResultScript($"DELETE FROM NonExistingTable");
            await channel.Batch.Execute();
        }
        catch (Exception err)
        {
            thrownError = err;
        }

        // Assert
        thrownError.Should().BeOfType<SqlProxyException>();
    }

    [Fact]
    public async Task Should_Throw_An_Error_For_InvalidSyntax_OnBatch()
    {
        // Arrange
        var client = await _testServer.GetClient(_testOutputHelper);
        Exception? thrownError = null;

        // Act
        try
        {
            using var channel = client.CreateChannel();
            channel.Batch.AddNoResultScript($"DELETE FRO TB_PRODUTO");
            await channel.Batch.Execute();
        }
        catch (Exception err)
        {
            thrownError = err;
        }

        // Assert
        thrownError.Should().BeOfType<SqlProxyException>();
    }
}