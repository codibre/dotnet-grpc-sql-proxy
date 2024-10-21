using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Codibre.GrpcSqlProxy.Common;
using static Dapper.SqlMapper;

namespace Codibre.GrpcSqlProxy.Client.Impl;

internal sealed class SqlProxyBatchQuery(ISqlProxyClientTunnel _tunnel) : ISqlProxyBatchQuery
{
    private static readonly FormattableString _beginTran = $"BEGIN TRAN;";
    private static readonly object _waiting = new();
    private readonly ScriptBuilder _builder = new();
    private readonly SqlProxyBatchQueryOptions? _options = null;
    private readonly List<Type> _types = [];
    private readonly Dictionary<object, object?> _results = [];
    private readonly List<Func<Reader, Task>> _hooks = [];
    private TransactionContext? _transaction = null;

    public string Sql => _builder.Sql;
    public int QueryCount => _builder.QueryCount;
    public int ParamCount => _builder.ParamCount;

    public void AddNoResultScript(FormattableString builtScript) => _builder.Add(builtScript);

    public void AddStartTransaction() => AddNoResultScript(_beginTran);
    public void AddFinishTransaction() => AddNoResultScript($"COMMIT");

    private bool SetResult(object token, object? result)
    {
        if (_results.TryGetValue(token, out var previous) && previous != _waiting) return false;
        _results[token] = result;
        return true;
    }

    private IResultHook<T> AddHook<T, R>(FormattableString builtScript, object token, Func<Reader, Task> hook)
    {
        if (_transaction is not null) _transaction.TransactionWithHooks = true;
        if (SetResult(token, _waiting))
        {
            _builder.Add(builtScript);
            _hooks.Add(hook);
            _types.Add(typeof(R));
        }

        return new ResultHookCallback<T>(() => Get<T>(token));
    }

    public IResultHook<T> QueryFirstHook<T>(FormattableString builtScript, object token)
    where T : class, new() => AddHook<T, T>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadFirstAsync<T>())
        );

    public IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript, object token)
    where T : class, new() => AddHook<T?, T>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadFirstOrDefaultAsync<T>())
        );

    public IResultHook<IEnumerable<T>> QueryHook<T>(FormattableString builtScript, object token)
    where T : class, new() => AddHook<IEnumerable<T>, T>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadAsync<T>().ToArrayAsync())
        );

    public async Task RunQueries(SqlProxyBatchQueryOptions? options)
    {
        if (_builder.QueryCount <= 0) return;
        var reader = _tunnel.QueryMultipleAsync(
            _builder.Sql,
            _types.Select(x => x.GetCachedSchema().Item2).ToArray(),
            new()
            {
                Compress = options?.Compress,
                PacketSize = options?.PacketSize,
                Params = _builder.Parameters
            }
        );
        var hookEnumerator = _hooks.GetEnumerator();
        while (!reader.IsConsumed)
        {
            if (!hookEnumerator.MoveNext())
                throw new InvalidDataException("Number of results is greater than the number of hooks!");
            var hook = hookEnumerator.Current;
            await hook(reader);
        }
        if (hookEnumerator.MoveNext()) throw new InvalidDataException("Number of results is lesser than the number of hooks!");
        ClearPendingRun();
    }

    public async Task Execute(TimeSpan? customTimeout = null)
    {
        if (_transaction?.TransactionWithHooks is true) throw new InvalidOperationException("Operation invalid for hooked transaction");
        if (_builder.QueryCount <= 0) return;
        await _tunnel.Execute(_builder.Sql, new()
        {
            Params = _builder.Parameters
        });
        ClearPendingRun();
    }

    public T Get<T>(object token)
    {
        var result = _results[token];
        if (result == _waiting) throw new InvalidOperationException("Query not executed yet!");
        if (result is T resultT) return resultT;
#pragma warning disable CS8603 // Possible null reference return.
        if (result is null) return default;
#pragma warning restore CS8603 // Possible null reference return.
        throw new InvalidOperationException($"Type {nameof(T)} incompatible with provided token");
    }

    public void Clear()
    {
        _results.Clear();
        ClearPendingRun();
    }

    private void ClearPendingRun()
    {
        if (_transaction is not null) _transaction.TransactionWithHooks = false;
        _builder.Clear();
        _hooks.Clear();
        _types.Clear();
    }

    private async IAsyncEnumerable<IList<KeyValuePair<TInput, TOutput>>> InternalPrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, ISqlProxyBatchQuery, ValueTask<TOutput>> PreRunQuery,
        SqlProxyBatchQueryOptions? options
    )
    {
        try
        {
            var enumerator = enumerable.GetEnumerator();
            var hasNext = enumerator.MoveNext();
            while (hasNext)
            {
                var result = new List<KeyValuePair<TInput, TOutput>>();
                do
                {
                    var current = enumerator.Current;
                    var preparedValue = await PreRunQuery(current, this);
                    result.Add(new KeyValuePair<TInput, TOutput>(current, preparedValue));
                    hasNext = enumerator.MoveNext();
                } while (hasNext && ParamCount + (options?.ParamMargin ?? 100) < _builder.ParamLimit);
                await RunQueries(options);
                yield return result;
            }
        }
        finally
        {
            ClearPendingRun();
        }
    }

    public IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, ISqlProxyBatchQuery, ValueTask<TOutput>> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null
    ) => InternalPrepareEnumerable(enumerable, PreRunQuery, options)
            .SelectMany(x => x.ToAsyncEnumerable());

    private async ValueTask InternalFlushTransaction()
    {
        if (!_transaction!.TransactionOpen)
        {
            _transaction.TransactionOpen = true;
            await _tunnel.BeginTransaction();
        }
        await ExecuteInTransaction();
    }
    public async ValueTask AddTransactionScript(FormattableString builtScript)
    {
        ValidateInTransaction();
        if (_builder.ParamCount + _transaction!.TransactionOptions.ParamMargin >= _builder.ParamLimit)
        {
            await InternalFlushTransaction();
        }
        _builder.Add(builtScript);
    }

    public ValueTask FlushTransaction()
    {
        ValidateInTransaction();
        return InternalFlushTransaction();
    }

    private void ValidateInTransaction()
    {
        if (_transaction is null) throw new InvalidOperationException("Must run inside RunInTransaction callback");
    }

    public async Task<T> RunInTransaction<T>(Func<ISqlProxyBatchQuery, ValueTask<T>> query, RunInTransactionOptions? options = null)
    {
        if (_transaction is not null) throw new InvalidOperationException("RunInTransaction Already called");
        if (_builder.QueryCount > 0) throw new InvalidOperationException("Query buffer not empty");
        _transaction = new(options);
        _tunnel.Start();
        try
        {
            var result = await query(this);
            if (_transaction.TransactionCanceled) await RollBack(_tunnel);
            else if (_transaction.TransactionOpen) await Commit(_tunnel);
            else await SendTransaction();
            return result;
        }
        catch (Exception)
        {
            if (_transaction.TransactionOpen) await _tunnel.Rollback();
            throw;
        }
        finally
        {
            _transaction = null;
        }
    }

    private async Task SendTransaction()
    {
        _builder.Prepend(_beginTran);
        AddFinishTransaction();
        await ExecuteInTransaction();
    }

    private async Task Commit(ISqlProxyClientTunnel _tunnel)
    {
        await ExecuteInTransaction();
        await _tunnel.Commit();
    }

    private async Task RollBack(ISqlProxyClientTunnel _tunnel)
    {
        await _tunnel.Rollback();
        Clear();
    }

    private async Task ExecuteInTransaction(
    )
    {
        if (_transaction!.TransactionWithHooks) await RunQueries(_options);
        else await Execute(_transaction?.TransactionOptions.CustomTimeout);
    }

    public void CancelTransaction()
    {
        ValidateInTransaction();
        if (_transaction is not null) _transaction.TransactionCanceled = true;
    }
}