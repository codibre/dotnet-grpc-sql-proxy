using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Codibre.GrpcSqlProxy.Client.Impl;

internal class ResultHook<T> : IResultHook<T>
{
    private readonly Func<T> _result;
    public T Result => _result();

    internal ResultHook(Func<T> result) => _result = result;
}