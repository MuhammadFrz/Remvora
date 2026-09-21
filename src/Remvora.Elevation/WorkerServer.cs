using System.IO.Pipes;
using Remvora.Contracts;
using Remvora.Contracts.Handshake;
using Remvora.Contracts.Operations;

namespace Remvora.Elevation;

/// <summary>
/// Authenticated named pipe IPC server hosting the elevated worker session.
/// </summary>
public sealed class WorkerServer
{
    private readonly string _pipeName;
    private readonly string _expectedNonce;
    private readonly ElevatedOperationExecutor _executor;

    public WorkerServer(
        string pipeName,
        string expectedNonce,
        ElevatedOperationExecutor executor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);

        _pipeName = pipeName;
        _expectedNonce = expectedNonce;
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var pipeServer = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        using var handshakeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(IpcConstants.HandshakeTimeoutMs));
        using var linkedHandshakeCts = CancellationTokenSource.CreateLinkedTokenSource(handshakeCts.Token, cancellationToken);

        await pipeServer.WaitForConnectionAsync(linkedHandshakeCts.Token).ConfigureAwait(false);

        // 1. Handshake
        var handshakeRequest = await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeRequest>(pipeServer, linkedHandshakeCts.Token).ConfigureAwait(false);
        if (handshakeRequest is null || !string.Equals(handshakeRequest.ClientNonce, _expectedNonce, StringComparison.Ordinal))
        {
            var rejection = new WorkerHandshakeResponse(
                IsAuthorized: false,
                ServerProcessId: Environment.ProcessId,
                RejectionReason: "Authentication failed: Client nonce mismatch.");

            await IpcWireProtocol.WriteMessageAsync(pipeServer, rejection, cancellationToken).ConfigureAwait(false);
            return;
        }

        var sessionToken = Guid.NewGuid().ToString("N");
        var handshakeResponse = new WorkerHandshakeResponse(
            IsAuthorized: true,
            ServerProcessId: Environment.ProcessId,
            SessionToken: sessionToken);

        await IpcWireProtocol.WriteMessageAsync(pipeServer, handshakeResponse, cancellationToken).ConfigureAwait(false);

        // 2. Process Operations
        while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var request = await IpcWireProtocol.ReadMessageAsync<ElevatedOperationRequest>(pipeServer, cancellationToken).ConfigureAwait(false);
                if (request is null)
                    break; // Client closed connection cleanly

                if (!string.Equals(request.SessionToken, sessionToken, StringComparison.Ordinal))
                {
                    var unauthorizedResult = new ElevatedOperationResult(
                        request.CorrelationId,
                        IsSuccess: false,
                        ErrorCode: 11, // WorkerRejected
                        ErrorMessage: "Session token rejected.");

                    await IpcWireProtocol.WriteMessageAsync(pipeServer, unauthorizedResult, cancellationToken).ConfigureAwait(false);
                    break;
                }

                var result = await _executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                await IpcWireProtocol.WriteMessageAsync(pipeServer, result, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Pipe broke or disconnected
                break;
            }
        }
    }
}
