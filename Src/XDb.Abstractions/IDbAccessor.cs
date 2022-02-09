using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XDb.Abstractions;

public interface IDbAccessor
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken ct = default);

    Task<T> SingleAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken ct = default);

    Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        CancellationToken ct = default);
}
