namespace Codibre.GrpcSqlProxy.Client.Impl;

public static class BatchQueryExtension
{

    public static IResultHook<IEnumerable<T>> QueryHook<T>(
        this ISqlProxyBatchQuery batchQuery, FormattableString builtScript)
    where T : class, new() => batchQuery.QueryHook<T>(builtScript, new object());

    public static IResultHook<T> QueryFirstHook<T>(
        this ISqlProxyBatchQuery batchQuery, FormattableString builtScript)
    where T : class, new() => batchQuery.QueryFirstHook<T>(builtScript, new object());
    public static IResultHook<T?> QueryFirstOrDefaultHook<T>(
        this ISqlProxyBatchQuery batchQuery, FormattableString builtScript)
    where T : class, new() => batchQuery.QueryFirstOrDefaultHook<T>(builtScript, new object());

    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        ISqlProxyBatchQuery batchQuery,
        Func<TInput, ISqlProxyBatchQuery, ValueTask<TOutput>> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null
    ) => batchQuery.PrepareEnumerable(enumerable, PreRunQuery, options);
    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareQueryBatch<TInput, TOutput>(
        this IEnumerable<TInput> enumerable,
        ISqlProxyBatchQuery batchQuery,
        Func<TInput, ISqlProxyBatchQuery, TOutput> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null
    ) => batchQuery.PrepareEnumerable(
        enumerable,
        PreRunQuery,
        options
    );

    public static IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        this ISqlProxyBatchQuery batchQuery,
        IEnumerable<TInput> enumerable,
        Func<TInput, ISqlProxyBatchQuery, TOutput> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null
    ) => batchQuery.PrepareEnumerable(
        enumerable,
        (input, bq) => new ValueTask<TOutput>(PreRunQuery(input, bq)),
        options
    );

    public static async Task<T> RunInTransaction<T>(
        this ISqlProxyBatchQuery batchQuery,
        Func<ISqlProxyBatchQuery, ValueTask<T>> query,
        RunInTransactionOptions? options = null
    )
    {
        T? result = default;
        if (options is null) await batchQuery.RunInTransaction(async (bq) =>
        {
            result = await query(bq);
        });
        else await batchQuery.RunInTransaction(async (bq) =>
        {
            result = await query(bq);
        }, options);
        return result!;
    }

    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery,
        Func<ValueTask> query,
        RunInTransactionOptions? options = null
    ) => batchQuery.RunInTransaction((_) => query(), options);
    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery,
        Action query,
        RunInTransactionOptions? options = null
    ) => batchQuery.RunInTransaction((_) => query(), options);

    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery,
        Action<ISqlProxyBatchQuery> query,
        RunInTransactionOptions? options = null
    ) => batchQuery.RunInTransaction((bq) =>
        {
            query(bq);
            return new ValueTask();
        }, options);

    public static async Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery,
        Func<ISqlProxyBatchQuery, ValueTask> query,
        RunInTransactionOptions? options = null
    ) => await batchQuery.RunInTransaction(async (bq) =>
    {
        await query(bq);
        return 1;
    }, options);
}