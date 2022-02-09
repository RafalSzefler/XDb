using System;
using System.Threading;
using System.Threading.Tasks;

namespace XDb.Abstractions;

public interface IDbUnitOfWork : IDbAccessor, IAsyncDisposable
{
    /// <summary>
    /// Commits the underlying transaction.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rollsback the underlying transaction.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
