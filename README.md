[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/build/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/test/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Actions Status](https://github.com/Codibre/dotnet-grpc-sql-proxy/workflows/lint/badge.svg)](https://github.com/Codibre/dotnet-grpc-sql-proxy/actions)
[![Test Coverage](https://api.codeclimate.com/v1/badges/a70dd4bf03f42c1f05b0/test_coverage)](https://codeclimate.com/github/codibre/dotnet-grpc-sql-proxy/test_coverage)
[![Maintainability](https://api.codeclimate.com/v1/badges/a70dd4bf03f42c1f05b0/maintainability)](https://codeclimate.com/github/codibre/dotnet-grpc-sql-proxy/maintainability)

# Codibre.Codibre.GrpcSqlProxy.Client

A library to connect to a grpc sql proxy


## Why?

SQlClient has an issue establishing concurrent connections, as described [here](https://github.com/dotnet/SqlClient/issues/601), which may not be a big deal in most cases, but if you have a high latency connection (like when you're connecting to a remote database), this can really escalate quickly on high demanding scenarios.
That said, this library tries to provide a workaround if it's not possible to keep the application close to the database, but you may be vulnerable to big loads of requests. There are other strategies that can also be used (cache, batching queries, etc). This repository just offer another option (very alpha stage), to deal with this situation until Microsoft resolve the issue once for all.


## How to use?

First of all, you have to prepare the server. The docker image is ready to be used [here](https://hub.docker.com/r/codibre/grpc-mutex-api). You need to put it as services in the same cloud and region of your SQL Server. 

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
await channel.Execute("BEGIN TRANSACTION");
await channel.Execute("INSERT INTO MyTable (Field1, Field2) VALUES ('test1', 123)");
await channel.Execute("COMMIT");
var result = await channel.QueryFirstOrDefault<TB_PRODUTO>("SELECT * FROM MyTable");
```

While using the same channel, every command you'll be sent the same connection, so, you can normally create transactions and, when the channel is disposed, the connections will be sent back to the connection pool to be reused.

The details for each connection are sent by the application client, so, if you have two applications with the exact same connection string, the chances are that they'll share the same connection pools.

Sql parameters are not supported yet but it's feasible to do so in the near future.

The result sets returned by the method **Query** are IAsyncEnumerable instances because the packets returned are processed on demand. This is done to keep the memory usage of the proxy controlled. For now, the methods available to execute sql commands are:

* Execute: To run a query without getting its result (feasible to insert, update, etc);
* Query: To return multiple rows;
* QueryFirst: To get one result and to throw an error if it's not found;
* QueryFirstOrDefault: To get one result or the default value for the type;

The model passed as a generic parameter for the query methods must be a reference type. You can't use int, bool, or other value types yet, but it can be done in the future.