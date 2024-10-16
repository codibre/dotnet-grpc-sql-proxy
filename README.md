[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/build/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/test/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/lint/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Test Coverage](https://api.codeclimate.com/v1/badges/a70dd4bf03f42c1f05b0/test_coverage)](https://codeclimate.com/github/codibre/dotnet-grpc-sql-proxy/test_coverage)
[![Maintainability](https://api.codeclimate.com/v1/badges/a70dd4bf03f42c1f05b0/maintainability)](https://codeclimate.com/github/codibre/dotnet-grpc-sql-proxy/maintainability)

# Codibre.GrpcSqlProxy.Client

A library to connect to a grpc sql proxy


## Why?

SQlClient has an issue establishing concurrent connections, as described [here](https://github.com/dotnet/SqlClient/issues/601), which may not be a big deal in most cases, but if you have a high latency connection (like when you're connecting to a remote database), this can really escalate quickly on high demanding scenarios.
That said, this library tries to provide a workaround if it's not possible to keep the application close to the database, but you may be vulnerable to big loads of requests. There are other strategies that can also be used (cache, batching queries, etc). This repository just offer another option (very alpha stage), to deal with this situation until Microsoft resolve the issue once for all.


## How to use?

First of all, you have to prepare the server. The docker image is ready to be used [here](https://hub.docker.com/r/codibre/dotnet-grpc-sql-proxy). You need to put it as services in the same cloud and region of your SQL Server. 

About the client, you can check the test folder for some other examples, but the usage is quite simple. First, create a the client. You can create it manually, like this:

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

You can also inject the client using its built in extensions:

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

While using the same channel, every command you'll be sent the same connection, so, you can normally create transactions and, when the channel is disposed, the connections will be sent back to the connection pool to be reused.

The details for each connection are sent by the application client, so, if you have two applications with the exact same connection string, the chances are that they'll share the same connection pools.

You can also pass parameters, and it's possible to change compression or packetSize options for a single request, as showed below:
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

The result sets returned by the method **Query** are IAsyncEnumerable instances because the packets returned are processed on demand. This is done to keep the memory usage of the proxy controlled. For now, the methods available to execute sql commands are:

* Execute: To run a query without getting its result (feasible to insert, update, etc);
* Query: To return multiple rows;
* QueryFirst: To get one result and to throw an error if it's not found;
* QueryFirstOrDefault: To get one result or the default value for the type;

The model passed as a generic parameter for the query methods must be a reference type. You can't use int, bool, or other value types yet, but it can be done in the future.

# Batch Operations

One of the features offered by this package is batch operation. With that, you can accumulate many sql operations in a single script, run them, and get separated, properly typed results. To do that as showed below:

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

If you want to accumulate many script but don't want to get any results. You can use `Execute` instead of `RunQueries`.
Theare limitations, though, to how many operations can be executed in a single script: the number of parameters. **SqlClient** only support the maximum of 2100 parameters, so, an error will be thrown if you created a script that have more than 2000 parameters. There tools, though, offered to deal seamlessly with that limitation.
The first one is the **PrepareEnumerable**. This method allow you to execute the batching operations
while iterating through an enumerable, and it will run the partial batches before the maximum number of parameters is reached. The only condition is that you don't reach it during the callback. Here's an example of its use:

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

Notice that parameters passed to the batch method are not interpolated string, but FormattableString.
They're used under the hood to build parameterized queries without the need for you to inform the parameters explicitly.

The second options is the **RunInTransaction** + **AddTransactionScript** methods. This one
servers the purpose of adding persistence operations preferentially in one round trip, but
if the parameter limit is about to be reached, the transaction will be split in many round trips
during the AddTransactionScript call (thus the ValueTask return). Here's an example:

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