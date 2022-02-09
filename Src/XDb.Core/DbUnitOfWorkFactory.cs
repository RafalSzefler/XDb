using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using XDb.Abstractions;

namespace XDb.Core;

internal sealed class DbUnitOfWorkFactory : IDbUnitOfWorkFactory
{
    private readonly DbConnection _connection;
    private readonly ThreadSafeMap<Type, UpdateParametersDelegate> _updateParametersDelegates;
    private readonly ThreadSafeMap<Type, object> _converToItemDelegates;
    private readonly Dictionary<Type, IValueConverter> _forwardValueConverters;
    private readonly INamePolicy _namePolicy;

    public DbUnitOfWorkFactory(
        DbConnection connection,
        Dictionary<Type, IValueConverter> forwardValueConverters,
        INamePolicy namePolicy)
    {
        _connection = connection;
        _forwardValueConverters = forwardValueConverters;
        _updateParametersDelegates = ThreadSafeMap<Type, UpdateParametersDelegate>.Create();
        _converToItemDelegates = ThreadSafeMap<Type, object>.Create();
        _namePolicy = namePolicy;
    }

    public async Task<IDbUnitOfWork> CreateAsync(IsolationLevel level, CancellationToken ct)
    {
        var transaction = await _connection
            .BeginTransactionAsync(level, ct)
            .ConfigureAwait(false);

        return new DbUnitOfWork(
            _connection,
            transaction,
            _updateParametersDelegates,
            _converToItemDelegates,
            _forwardValueConverters,
            _namePolicy);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
