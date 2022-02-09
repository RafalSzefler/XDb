using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace XDb.Abstractions;

public interface IDbUnitOfWorkFactory : IAsyncDisposable
{
    Task<IDbUnitOfWork> CreateAsync(
        IsolationLevel level = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);
}
