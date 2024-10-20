using Codibre.GrpcSqlProxy.Common;

namespace Codibre.GrpcSqlProxy.Client;

public interface ISqlProxyBatchQuery
{
    /// <summary>
    /// Deve ser usado para adicionar um script que não retorna resultado ao batch
    /// </summary>
    /// <param name="builtScript">A query a acumular</param>
    void AddNoResultScript(FormattableString builtScript);
    /// <summary>
    /// Método para montar lotes de transação dentro do callback do RunInTransaction.
    /// Só deve ser chamado dentro do callback, irá lançar uma exceção se for chamado
    /// de fora.
    /// Caso o limite de parâmetros seja atingido, o método irá forçar a abertura da
    /// transação (caso já não tenha feito isso antes) e irá enviar o batch acumulado
    /// até o momento, para que um novo batch possa ser acumulado, e a transação ser
    /// fechada no final. Caso os scripts acumulados nunca ultrapassem o limite de
    /// parâmetros, um único batch será enviado ao final do callback do RunInTransaction
    /// </summary>
    /// <param name="builtScript">A query a acumular</param>
    /// <returns>Uma ValueTask que deve ser aguardada</returns>
    ValueTask AddTransactionScript(FormattableString builtScript);
    /// <summary>
    /// Força uma transação acumulada em batch a já enviar para o banco os comandos
    /// acumulados até o momento. Atenção: não encerra a transação
    /// </summary>
    /// <returns></returns>
    ValueTask FlushTransaction();
    /// <summary>
    /// Adiciona uma instrução BEGIN TRANSACTION ao batch
    /// </summary>
    void AddStartTransaction();
    /// <summary>
    /// Adiciona uma instrução COMMIT ao batch, para finalizar a transação
    /// </summary>
    void AddFinishTransaction();
    IResultHook<IEnumerable<T>> QueryHook<T>(FormattableString builtScript) where T : class, new();
    IResultHook<IEnumerable<T>> QueryHook<T>(FormattableString builtScript, object token) where T : class, new();
    IResultHook<T> QueryFirstHook<T>(FormattableString builtScript) where T : class, new();
    IResultHook<T> QueryFirstHook<T>(FormattableString builtScript, object token) where T : class, new();
    IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript) where T : class, new();
    IResultHook<T?> QueryFirstOrDefaultHook<T>(FormattableString builtScript, object token) where T : class, new();
    Task RunQueries(SqlProxyBatchQueryOptions? options = null);
    Task Execute(TimeSpan? customTimeout = null);

    T Get<T>(object token);

    void Clear();

    string Sql { get; }
    int QueryCount { get; }
    int ParamCount { get; }

    IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, ISqlProxyBatchQuery, ValueTask<TOutput>> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null,
        int paramMargin = 100
    );
    IAsyncEnumerable<KeyValuePair<TInput, TOutput>> PrepareEnumerable<TInput, TOutput>(
        IEnumerable<TInput> enumerable,
        Func<TInput, ISqlProxyBatchQuery, TOutput> PreRunQuery,
        SqlProxyBatchQueryOptions? options = null,
        int paramMargin = 100
    );

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
    Task RunInTransaction(Func<ISqlProxyBatchQuery, ValueTask> query, int paramMargin = 100);
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
    Task RunInTransaction(Func<ValueTask> query, int paramMargin = 100);
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
    Task RunInTransaction(Action<ISqlProxyBatchQuery> query, int paramMargin = 100);
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
    Task RunInTransaction(Func<ISqlProxyBatchQuery, ValueTask> query, RunInTransactionOptions options);
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
    Task RunInTransaction(Func<ValueTask> query, RunInTransactionOptions options);
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
    Task RunInTransaction(Action query, RunInTransactionOptions options);
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
    Task<T> RunInTransaction<T>(Func<ValueTask<T>> query, RunInTransactionOptions? options = null);
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
    Task<T> RunInTransaction<T>(Func<ISqlProxyBatchQuery, ValueTask<T>> query, RunInTransactionOptions? options = null);

    /// <summary>
    /// Cancela transação que está sendo montada no batch.
    /// Deve ser chamado somente dentro de um callback da RunInTransaction
    /// </summary>
    void CancelTransaction();
}