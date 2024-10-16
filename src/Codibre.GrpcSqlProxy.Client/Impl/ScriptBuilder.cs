using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using DapperQueryBuilder;
using InterpolatedSql.Dapper.SqlBuilders;
using Microsoft.Data.SqlClient;
using static Dapper.SqlMapper;

namespace Codibre.GrpcSqlProxy.Client.Impl;

public class ScriptBuilder
{
    public int QueryCount { get; private set; }
    public int ParamLimit { get; } = 2100;
    private static readonly SqlConnection _connection = new();
    private QueryBuilder _queryBuilder;
    public FormattableString FormattableQuery => _queryBuilder;

    internal ScriptBuilder(
    )
    {
        _queryBuilder = _connection.QueryBuilder();
    }

    public string Sql => _queryBuilder.Sql;

    public int ParamCount => _queryBuilder.Parameters.Count;
    public Dictionary<string, object> Parameters => _queryBuilder.Parameters.ParameterNames
        .ToDictionary(x => x, _queryBuilder.Parameters.Get<object>);

    public void Add(FormattableString query)
    {
        if (ParamCount + query.ArgumentCount > ParamLimit)
            throw new InvalidOperationException("Parameter limit reached");
        QueryCount++;
        _queryBuilder += query;
        EnsureSemiColon();
    }

    private void EnsureSemiColon()
    {
        if (!_queryBuilder.Format.EndsWith(';')) _queryBuilder += $";";
    }

    public void Clear()
    {
        QueryCount = 0;
        _queryBuilder = _connection.QueryBuilder();
    }

    public void Prepend(FormattableString query)
    {
        QueryCount = 1;
        var current = _queryBuilder;
        _queryBuilder = new QueryBuilder(_connection, query);
        EnsureSemiColon();
        Add(current);
    }
}