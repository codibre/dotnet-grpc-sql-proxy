using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Codibre.GrpcSqlProxy.Client.Impl;

internal sealed class TransactionContext(RunInTransactionOptions? options)
{
    public bool TransactionOpen { get; set; } = false;
    public bool TransactionCanceled { get; set; } = false;
    public bool TransactionWithHooks { get; set; } = false;
    public RunInTransactionOptions TransactionOptions { get; set; } = options ?? new();
}