namespace Codibre.GrpcSqlProxy.Client;

public class RunInTransactionOptions
{
    public int ParamMargin { get; set; } = 100;
    public TimeSpan? CustomTimeout { get; set; }
}