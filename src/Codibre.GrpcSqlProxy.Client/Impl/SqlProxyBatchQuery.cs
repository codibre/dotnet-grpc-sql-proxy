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
    private bool _inTransaction = false;
    private bool _transactionOpen = false;
    private bool _transactionCanceled = false;
    private bool _transactionWithHooks = false;
    private RunInTransactionOptions? _transactionOptions = null;

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
        if (_inTransaction) _transactionWithHooks = true;
        if (SetResult(token, _waiting))
        {
            _builder.Add(builtScript);
            _hooks.Add(hook);
            _types.Add(typeof(R));
        }

        return new ResultHookCallback<T>(() => Get<T>(token));
    }

    public IResultHook<T> QueryFirstHook<T>(FormattableString builtScript)
    where T : class, new() => QueryFirstHook<T>(builtScript, new object());

    public IResultHook<T> QueryFirstHook<T>(FormattableString builtScript, object token)
    where T : class, new() => AddHook<T, T>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadFirstAsync<T>())
        );

    public IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript)
    where T : class, new() => QueryFirstOrDefaultHook<T>(builtScript, new object());
    public IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript, object token)
    where T : class, new() => AddHook<T?, T>(builtScript, token,
            async (reader) => SetResult(token, await reader.ReadFirstOrDefaultAsync<T>())
        );

    public IResultHook<IEnumerable<T>> QueryHook<T>(FormattableString builtScript)
    where T : class, new() => QueryHook<T>(builtScript, new object());

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
        if (_transactionWithHooks) throw new InvalidOperationException("Operation invalid for hooked transaction");
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
        _transactionWithHooks = false;
        _builder.Clear();
        _hooks.Clear();
        _types.Clear();
    }

    private async IAsyncEnumerable<IList<KeyValuePair<TInput, TOutput>>> InternalPrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, ISqlProxyBatchQuery, ValueTask<TOutput>> PreRunQuery,
        SqlProxyBatchQueryOptions? options,
        int paramMargin = 100
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
                } while (hasNext && ParamCount + paramMargin < _builder.ParamLimit);
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
        SqlProxyBatchQueryOptions? options = null,
        int paramMargin = 100
    ) => InternalPrepareEnumerable(enumerable, PreRunQuery, options, paramMargin)
            .SelectMany(x => x.ToAsyncEnumerable());
    public IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, ISqlProxyBatchQuery, TOutput> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null,
        int paramMargin = 100
    ) => PrepareEnumerable(
        enumerable,
        (input, bq) => new ValueTask<TOutput>(PreRunQuery(input, bq)),
        options,
        paramMargin
    );

    private async ValueTask InternalFlushTransaction()
    {
        if (!_transactionOpen)
        {
            _transactionOpen = true;
            await _tunnel.BeginTransaction();
        }
        await ExecuteInTransaction();
    }
    public async ValueTask AddTransactionScript(FormattableString builtScript)
    {
        ValidateInTransaction();
        if (_builder.ParamCount + _transactionOptions!.ParamMargin >= _builder.ParamLimit)
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
        if (!_inTransaction) throw new InvalidOperationException("Must run inside RunInTransaction callback");
    }

    public Task RunInTransaction(Func<ISqlProxyBatchQuery, ValueTask> query, int paramMargin = 100)
        => RunInTransaction(query, new RunInTransactionOptions
        {
            ParamMargin = paramMargin
        });

    public Task RunInTransaction(Func<ValueTask> query, int paramMargin = 100)
        => RunInTransaction((_) => query(), paramMargin);

    public Task RunInTransaction(Action<ISqlProxyBatchQuery> query, int paramMargin = 100)
        => RunInTransaction((bq) =>
        {
            query(bq);
            return new ValueTask();
        }, paramMargin);

    public Task RunInTransaction(Action query, int paramMargin = 100)
        => RunInTransaction((bq) =>
        {
            query();
            return new ValueTask();
        }, paramMargin);

    public async Task RunInTransaction(Func<ISqlProxyBatchQuery, ValueTask> query, RunInTransactionOptions options)
    {
        if (_inTransaction) throw new InvalidOperationException("RunInTransaction Already called");
        if (_builder.QueryCount > 0) throw new InvalidOperationException("Query buffer not empty");
        try
        {
            _inTransaction = true;
            _transactionOpen = false;
            _transactionCanceled = false;
            _transactionWithHooks = false;
            _transactionOptions = options;
            await query(this);
            if (_transactionCanceled)
            {
                await _tunnel.Rollback();
                Clear();
            }
            else if (_transactionOpen)
            {
                await ExecuteInTransaction();
                await _tunnel.Commit();
            }
            else
            {
                _builder.Prepend(_beginTran);
                AddFinishTransaction();
                await ExecuteInTransaction();
            }
        }
        catch (Exception)
        {
            if (_transactionOpen) await _tunnel.Rollback();
            throw;
        }
        finally
        {
            _inTransaction = false;
            _transactionOpen = false;
            _transactionCanceled = false;
            _transactionWithHooks = false;
            _transactionOptions = null;
        }
    }

    private async Task ExecuteInTransaction(
    )
    {
        if (_transactionWithHooks) await RunQueries(_options);
        else await Execute(_transactionOptions!.CustomTimeout);
    }

    public Task RunInTransaction(Func<ValueTask> query, RunInTransactionOptions options)
        => RunInTransaction((_) => query(), options);

    public Task RunInTransaction(Action query, RunInTransactionOptions options)
        => RunInTransaction((_) =>
        {
            query();
            return new ValueTask();
        }, options);

    public async Task<T> RunInTransaction<T>(Func<ValueTask<T>> query, RunInTransactionOptions? options = null)
        => await RunInTransaction((_) => query(), options);

    public async Task<T> RunInTransaction<T>(Func<ISqlProxyBatchQuery, ValueTask<T>> query, RunInTransactionOptions? options = null)
    {
        T? result = default;
        if (options is null) await RunInTransaction(async (bq) =>
        {
            result = await query(bq);
        });
        else await RunInTransaction(async (bq) =>
        {
            result = await query(bq);
        }, options);
        return result!;
    }

    public void CancelTransaction()
    {
        ValidateInTransaction();
        _transactionCanceled = true;
    }
}

public static class BatchQueryExtension
{
    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        ISqlProxyBatchQuery batchQuery,
        Func<TInput, ISqlProxyBatchQuery, ValueTask<TOutput>> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null,
        int paramMargin = 100
    ) => batchQuery.PrepareEnumerable(enumerable, PreRunQuery, options, paramMargin);
    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        ISqlProxyBatchQuery batchQuery,
        Func<TInput, ISqlProxyBatchQuery, TOutput> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null,
        int paramMargin = 100
    ) => batchQuery.PrepareEnumerable(
        enumerable,
        (input, bq) => new ValueTask<TOutput>(PreRunQuery(input, bq)),
        options,
        paramMargin
    );
}