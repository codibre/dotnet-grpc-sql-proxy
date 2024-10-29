namespace Codibre.GrpcSqlProxy.Test;

[CollectionDefinition("TestServerCollection")]
public class TestServerCollection(TestServer testServer) : ICollectionFixture<TestServer>
{
    public TestServer TestServer { get; } = testServer;
}