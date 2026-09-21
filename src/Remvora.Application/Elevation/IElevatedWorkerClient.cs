using Remvora.Contracts;
using Remvora.Contracts.Operations;
using Remvora.Core.Domain.Results;

namespace Remvora.Application.Elevation;

/// <summary>
/// Active authenticated session with the privileged worker process.
/// </summary>
public interface IElevatedSession : IAsyncDisposable
{
    int ServerProcessId { get; }
    string SessionToken { get; }

    Task<ElevatedOperationResult> ExecuteAsync(
        ElevatedCommandType commandType,
        string target,
        string? secondaryTarget = null,
        bool backupRequested = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Client interface for launching and connecting to the elevated worker process.
/// </summary>
public interface IElevatedWorkerClient
{
    Task<OperationResult<IElevatedSession>> StartSessionAsync(
        bool requestElevation = true,
        CancellationToken cancellationToken = default);
}
