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
        (input, bq) => new ValueTask<TOutput>(PreRunQuery(input, bq)),
        options
    );

    /// <summary>
    /// Usa o callback para montar uma transação que será executada em lote com o banco.
    /// Este método quebrará o lote em vários caso o número de parâmetros fique grande
    /// demais para o driver do SQL suportar o comando, mas, para isso, é preciso inserir
    /// as operações usando o método AddTransactionScript, que irá partir os comandos
    /// acumulados em vários scripts e enviá-los sequencialmente, se necessário.
    /// </summary>
    /// <param name="query">O callback que irá executar a pesquisa.</param>
    /// <param name="options">Para definir margem de segurança e um timeout de comando customizado</param>
    /// <returns>Retorna uma task, que deve ser aguardada, que irá executar a transação</returns>
    public static async Task<T> RunInTransaction<T>(
        this ISqlProxyBatchQuery batchQuery, Func<ISqlProxyBatchQuery, ValueTask<T>> query, RunInTransactionOptions? options = null)
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


    /// <summary>
    /// Usa o callback para montar uma transação que será executada em lote com o banco.
    /// Este método quebrará o lote em vários caso o número de parâmetros fique grande
    /// demais para o driver do SQL suportar o comando, mas, para isso, é preciso inserir
    /// as operações usando o método AddTransactionScript, que irá partir os comandos
    /// acumulados em vários scripts e enviá-los sequencialmente, se necessário.
    /// </summary>
    /// <param name="query">O callback que irá executar a pesquisa. Recebe uma referência ao próprio batchQuery</param>
    /// <param name="paramMargin">Margem de segurança para o limite de parâmetros, isto é, o script será quebrado se a quantidade ultrapassar o máximo - essa margem. O padrão é 100</param>
    /// <returns>Retorna uma task, que deve ser aguardada, que irá executar a transação</returns>
    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery, Func<ISqlProxyBatchQuery, ValueTask> query, int paramMargin = 100)
        => batchQuery.RunInTransaction(query, new RunInTransactionOptions
        {
            ParamMargin = paramMargin
        });

    /// <summary>
    /// Usa o callback para montar uma transação que será executada em lote com o banco.
    /// Este método quebrará o lote em vários caso o número de parâmetros fique grande
    /// demais para o driver do SQL suportar o comando, mas, para isso, é preciso inserir
    /// as operações usando o método AddTransactionScript, que irá partir os comandos
    /// acumulados em vários scripts e enviá-los sequencialmente, se necessário.
    /// </summary>
    /// <param name="query">O callback que irá executar a pesquisa.</param>
    /// <param name="paramMargin">Margem de segurança para o limite de parâmetros, isto é, o script será quebrado se a quantidade ultrapassar o máximo - essa margem. O padrão é 100</param>
    /// <returns>Retorna uma task, que deve ser aguardada, que irá executar a transação</returns>
    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery, Func<ValueTask> query, int paramMargin = 100)
        => batchQuery.RunInTransaction((_) => query(), paramMargin);

    /// <summary>
    /// Usa o callback para montar uma transação que será executada em lote com o banco.
    /// Este método quebrará o lote em vários caso o número de parâmetros fique grande
    /// demais para o driver do SQL suportar o comando, mas, para isso, é preciso inserir
    /// as operações usando o método AddTransactionScript, que irá partir os comandos
    /// acumulados em vários scripts e enviá-los sequencialmente, se necessário.
    /// </summary>
    /// <param name="query">O callback que irá executar a pesquisa. Recebe uma referência ao próprio batchQuery</param>
    /// <param name="paramMargin">Margem de segurança para o limite de parâmetros, isto é, o script será quebrado se a quantidade ultrapassar o máximo - essa margem. O padrão é 100</param>
    /// <returns>Retorna uma task, que deve ser aguardada, que irá executar a transação</returns>
    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery, Action<ISqlProxyBatchQuery> query, int paramMargin = 100)
        => batchQuery.RunInTransaction((bq) =>
        {
            query(bq);
            return new ValueTask();
        }, paramMargin);

    /// <summary>
    /// Usa o callback para montar uma transação que será executada em lote com o banco.
    /// Este método quebrará o lote em vários caso o número de parâmetros fique grande
    /// demais para o driver do SQL suportar o comando, mas, para isso, é preciso inserir
    /// as operações usando o método AddTransactionScript, que irá partir os comandos
    /// acumulados em vários scripts e enviá-los sequencialmente, se necessário.
    /// </summary>
    /// <param name="query">O callback que irá executar a pesquisa.</param>
    /// <param name="paramMargin">Para definir margem de segurança e um timeout de comando customizado</param>
    /// <returns>Retorna uma task, que deve ser aguardada, que irá executar a transação</returns>
    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery, Action query, int paramMargin = 100)
        => batchQuery.RunInTransaction((bq) =>
        {
            query();
            return new ValueTask();
        }, paramMargin);

    /// <summary>
    /// Usa o callback para montar uma transação que será executada em lote com o banco.
    /// Este método quebrará o lote em vários caso o número de parâmetros fique grande
    /// demais para o driver do SQL suportar o comando, mas, para isso, é preciso inserir
    /// as operações usando o método AddTransactionScript, que irá partir os comandos
    /// acumulados em vários scripts e enviá-los sequencialmente, se necessário.
    /// </summary>
    /// <param name="query">O callback que irá executar a pesquisa.</param>
    /// <param name="options">Para definir margem de segurança e um timeout de comando customizado</param>
    /// <returns>Retorna uma task, que deve ser aguardada, que irá executar a transação</returns>
    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery,
        Func<ValueTask> query,
        RunInTransactionOptions options
    ) => batchQuery.RunInTransaction((_) => query(), options);

    /// <summary>
    /// Usa o callback para montar uma transação que será executada em lote com o banco.
    /// Este método quebrará o lote em vários caso o número de parâmetros fique grande
    /// demais para o driver do SQL suportar o comando, mas, para isso, é preciso inserir
    /// as operações usando o método AddTransactionScript, que irá partir os comandos
    /// acumulados em vários scripts e enviá-los sequencialmente, se necessário.
    /// </summary>
    /// <param name="query">O callback que irá executar a pesquisa.</param>
    /// <param name="options">Para definir margem de segurança e um timeout de comando customizado</param>
    /// <returns>Retorna uma task, que deve ser aguardada, que irá executar a transação</returns>
    public static Task RunInTransaction(
        this ISqlProxyBatchQuery batchQuery,
        Action query, RunInTransactionOptions options
    ) => batchQuery.RunInTransaction((_) =>
        {
            query();
            return new ValueTask();
        }, options);

    public static async Task<T> RunInTransaction<T>(
        this ISqlProxyBatchQuery batchQuery,
        Func<ValueTask<T>> query,
        RunInTransactionOptions? options = null
    ) => await batchQuery.RunInTransaction((_) => query(), options);

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
}