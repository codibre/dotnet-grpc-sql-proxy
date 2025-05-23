[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/build/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/test/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/lint/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Test Coverage](https://api.codeclimate.com/v1/badges/1a86a06e659ab9e87820/test_coverage)](https://codeclimate.com/github/codibre/dotnet-grpc-sql-proxy/test_coverage)
[![Maintainability](https://api.codeclimate.com/v1/badges/1a86a06e659ab9e87820/maintainability)](https://codeclimate.com/github/codibre/dotnet-grpc-sql-proxy/maintainability)

# Codibre.GrpcSqlProxy.Client

A library to connect to a grpc sql proxy


## Why?

SQLClient has an issue establishing concurrent connections, as described [here](https://github.com/dotnet/SqlClient/issues/601). While this may not be a significant problem in most cases, it can escalate quickly in high-demand scenarios, especially with high-latency connections, such as when connecting to a remote database.

This library provides a workaround for situations where keeping the application close to the database is not feasible, though it may still be vulnerable to heavy loads of requests. Other strategies, such as caching and batching queries, can also be employed. This repository offers an additional option (currently in a very alpha stage) to address this issue until Microsoft resolves it once and for all.


## How to use?

First, you need to prepare the server. The Docker image is ready to be used [here](https://hub.docker.com/r/codibre/dotnet-grpc-sql-proxy). Ensure you deploy it as a service in the same cloud and region as your SQL Server.

For the client, you can check the test folder for additional examples, but the usage is quite simple. First, create the client manually, like this:

```c#
var client = new GrpcSqlProxyClient(
    new SqlProxyClientOptions(
        _url, // Grpc Proxy Url
        sqlConnection // Sql Connection String
    ) {
        Compress = true, // Where the packets must be compressed
        PacketSize = 2000 // How many rows are in each packet (default 1000) 
    }
);
```

You can also inject the client using its built-in extensions:

```c#
servicesCollection.AddGrpcSqlProxy();
```

The configuration will be obtained from **IConfiguration**. You need to declare it like this:

```json
{
    "ConnectionStrings": {
        "SqlConnection": "Server=127.0.0.1;Database=SampleDb;User Id=sa;Password=Sa12345!;Trusted_Connection=False;TrustServerCertificate=True;Integrated Security=False;"
    },
    "GrpcSqlProxy": {
        "Url": "Proxy Url",
        "Compress": "True",
        "PacketSize": "2000"
    }
}
```

Now that you have a client created, you have to establish a channel:

```c#
using var channel = client.CreateChannel();
```

Finally, you can run your queries:

```c#
await channel.BeginTransaction();
await channel.Execute("INSERT INTO MyTable (Field1, Field2) VALUES ('test1', 123)");
await channel.Commit();
var result = await channel.QueryFirstOrDefault<TB_PRODUTO>("SELECT * FROM MyTable");
```

While using the same channel, every command will use the same connection. This allows you to create transactions normally, and when the channel is disposed, the connections will be returned to the connection pool for reuse.

The details for each connection are provided by the application client. Therefore, if you have two applications with the exact same connection string, they will likely share the same connection pools.

You can also pass parameters, and it's possible to change compression or packet size options for a single request, as shown below:

```c#
var result = await channel.QueryFirstOrDefault<TB_PRODUTO>("SELECT * FROM MyTable WHERE id = @Id", new()
{
    Params = new
    {
        Id = 1
    },
    PacketSize = 10,
    Compress = false
});
```

The result sets returned by the method **Query** are IAsyncEnumerable instances because the packets are processed on demand. This approach helps control the memory usage of the proxy. Currently, the available methods to execute SQL commands are:

* Execute: Runs a query without returning its result (suitable for insert, update, etc.).
* Query: Returns multiple rows.
* QueryFirst: Retrieves one result and throws an error if not found.
* QueryFirstOrDefault: Retrieves one result or the default value for the type.

The model passed as a generic parameter for the query methods must be a reference type. You can't use int, bool, or other value types yet, but this feature may be added in the future.

# Batch Operations

One of the features offered by this package is batch operation. With it, you can accumulate many SQL operations in a single script, run them, and get separate, properly typed results. Here's how to do that:

```c#
// Prepare operations
channel.AddNoScriptResult($"UPDATE MyTable SET Field3 = {myValue} WHERE Id = {myId}");
var itemsHook = channel.QueryHook($"SELECT * FROM MyTable2 WHERE parentId = {myId}");
var singleItemHook = channel.QueryFirstHook($"SELECT TOP 1 * FROM MyTable3 WHERE parentId = {myId}");
var optionalSingleItemHook = channel.QueryFirstOrDefaultHook(@$"SELECT
    TOP 1 *
    FROM MyTable4
    WHERE parentId = {myId}"
);
// Execute all the accumulated operations
await channel.RunQueries();

// Get The desired results
var items = itemsHook.Result;
var singleItem = singleItemHook.Result;
var optionalSingleItem = optionalSingleItemHook.Result;
```

If you want to accumulate many scripts but don't want to get any results, you can use `Execute` instead of `RunQueries`.

There are limitations, though, to how many operations can be executed in a single script: the number of parameters. **SqlClient** only supports a maximum of 2100 parameters, so an error will be thrown if you create a script with more than 2000 parameters. There are tools, though, offered to deal seamlessly with that limitation.

The first one is **PrepareEnumerable**. This method allows you to execute the batching operations while iterating through an enumerable, and it will run the partial batches before the maximum number of parameters is reached. The only condition is that you don't reach it during the callback. Here's an example of its use:

```c#
await channel.Batch.PrepareEnumerable(pars, async (i, b) =>
    {
        return b.QueryFirstHook<MyTable>(@$"SELECT *
            FROM MyTable
            WHERE Id = {i}");
    })
    // The result if a Enumerable of KeyValue where
    // the Key is the input, and the value the result of
    // the callback
    .ForEachAsync(x => list.Add((x.Key, x.Value.Result)));
```

Notice that parameters passed to the batch method are not interpolated strings, but `FormattableString`. They're used under the hood to build parameterized queries without the need for you to specify the parameters explicitly.

The second option is the **RunInTransaction** + **AddTransactionScript** methods. These serve the purpose of adding persistence operations preferentially in one round trip. However, if the parameter limit is about to be reached, the transaction will be split into multiple round trips during the AddTransactionScript call (hence the ValueTask return). Here's an example:

```c#
await channel.RunInTransaction(async () =>
{
    await channel.AddTransactionScript(@$"UPDATE MyTable SET
        Field1 = {Value1},
        Field2 = {Value2},
        Field3 = {Value3}
    WHERE id = {Id1}");
    await channel.AddTransactionScript(@$"UPDATE MyTable2 SET
        Field1 = {Value4},
        Field2 = {Value5},
        Field3 = {Value6}
    WHERE id = {Id2}");
    await channel.AddTransactionScript(@$"UPDATE MyTable3 SET
        Field1 = {Value6},
        Field2 = {Value7},
        Field3 = {Value8}
    WHERE id = {Id3}");
});
```