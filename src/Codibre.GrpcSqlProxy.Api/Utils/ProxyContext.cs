using System.Data.Common;
using System.Transactions;
using Microsoft.Data.SqlClient;

namespace Codibre.GrpcSqlProxy.Api.Utils
{
    internal class ProxyContext
    {
        public string? ConnectionString { get; set; } = null;
        public SqlConnection? Connection { get; set; } = null;
        public DbTransaction? Transaction { get; set; } = null;
    }
}