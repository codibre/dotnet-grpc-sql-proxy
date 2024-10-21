namespace Codibre.GrpcSqlProxy.Client;

public class SqlProxyException : Exception
{
    private readonly string? _stack;
    public SqlProxyException(string message) : base(message)
    {
        _stack = null;
    }
    public SqlProxyException(Exception ex) : base(ex.Message)
    {
        _stack = ex.StackTrace;
    }

    public override string? StackTrace => _stack ?? base.StackTrace;
}