using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using XDb.Abstractions;

namespace XDb.Core;

internal sealed class DbUnitOfWork : IDbUnitOfWork
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CapacityIncrease(int previousCapacity)
        => Math.Min(4 * previousCapacity, 1 << 14);

    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly ThreadSafeMap<Type, UpdateParametersDelegate> _updateParametersDelegates;
    private readonly ThreadSafeMap<Type, object> _convertToItemDelegates;
    private readonly Dictionary<Type, IValueConverter> _forwardValueConverters;
    private readonly INamePolicy _namePolicy;

    public DbUnitOfWork(
        DbConnection connection,
        DbTransaction transaction,
        ThreadSafeMap<Type, UpdateParametersDelegate> updateParametersDelegates,
        ThreadSafeMap<Type, object> convertToItemDelegates,
        Dictionary<Type, IValueConverter> forwardValueConverters,
        INamePolicy namePolicy)
    {
        _connection = connection;
        _transaction = transaction;
        _updateParametersDelegates = updateParametersDelegates;
        _convertToItemDelegates = convertToItemDelegates;
        _forwardValueConverters = forwardValueConverters;
        _namePolicy = namePolicy;
    }

    public Task CommitAsync(CancellationToken ct) => _transaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct) => _transaction.RollbackAsync(ct);

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();

    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sql))
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var cmd = CreateDbCommand(sql, parameters);
        return cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sql))
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var cmd = CreateDbCommand(sql, parameters);
        var reader = await cmd
            .ExecuteReaderAsync(CommandBehavior.SingleResult, ct)
            .ConfigureAwait(false);

        var totalSize = 0;
        List<List<T>>? results = null;

        try
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var schema = reader.GetColumnSchema();
                var schemaDict = new Dictionary<string, DbColumn>(schema.Count);
                foreach (var column in schema)
                {
                    schemaDict[column.ColumnName] = column;
                }

                results = new List<List<T>>(8);
                results.Add(new List<T>(16));

                do
                {
                    var currentList = results[results.Count - 1];
                    if (currentList.Count == currentList.Capacity)
                    {
                        currentList = new List<T>(CapacityIncrease(currentList.Capacity));
                        results.Add(currentList);
                    }

                    totalSize++;

                    var item = ConvertToItem<T>(reader, schemaDict);
                    currentList.Add(item);
                }
                while (await reader.ReadAsync(ct).ConfigureAwait(false));
            }
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }

        if (totalSize == 0)
        {
            return Array.Empty<T>();
        }

        return new NestedList<T>(results!, totalSize);
    }

    public async Task<T> SingleAsync<T>(string sql, object? parameters, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sql))
        {
            throw new ArgumentNullException(nameof(sql));
        }

        var cmd = CreateDbCommand(sql, parameters);
        var reader = await cmd
            .ExecuteReaderAsync(CommandBehavior.SingleResult, ct)
            .ConfigureAwait(false);

        T resultItem;

        try
        {
            var schema = reader.GetColumnSchema();
            var schemaDict = new Dictionary<string, DbColumn>(schema.Count);
            foreach (var column in schema)
            {
                schemaDict[column.ColumnName] = column;
            }

            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new NoResultException($"{typeof(T)}");
            }

            resultItem = ConvertToItem<T>(reader, schemaDict);

            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new MultipleResultsException($"{typeof(T)}");
            }
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }

        return resultItem;
    }

    private DbCommand CreateDbCommand(string sql, object? parameters)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;

        if (parameters != null)
        {
            var parametersType = parameters.GetType();
            UpdateParametersDelegate updateParameters;

            if (!_updateParametersDelegates.TryGetValue(parametersType, out updateParameters))
            {
                // This if is only to avoid unnecessary anonymous function allocation
                // in case delegate actually exists.
                Func<Type, UpdateParametersDelegate> valueFactory =
                    t => UpdateParametersDelegateBuilder.Build(t, _forwardValueConverters);

                updateParameters = _updateParametersDelegates.GetOrAdd(parametersType, valueFactory);
            }

            updateParameters(cmd, parameters);
        }

        return cmd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T ConvertToItem<T>(DbDataReader reader, Dictionary<string, DbColumn> schema)
    {
        object convertToItem;
        var type = typeof(T);

        if (!_convertToItemDelegates.TryGetValue(type, out convertToItem))
        {
            // This if is only to avoid unnecessary anonymous function allocation
            // in case delegate actually exists.
            Func<Type, ConvertToItemDelegate<T>> valueFactory =
                _ => ConvertToItemDelgateBuilder.Build<T>(_forwardValueConverters, _namePolicy);

            convertToItem = _convertToItemDelegates.GetOrAdd(type, valueFactory);
        }

        var converter = (ConvertToItemDelegate<T>)convertToItem;
        return converter(reader, schema);
    }
}
