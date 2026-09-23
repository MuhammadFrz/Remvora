using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Remvora.Contracts;
using Remvora.Contracts.Handshake;
using Remvora.Contracts.Operations;

namespace Remvora.Elevation;

/// <summary>
/// Authenticated named pipe IPC server hosting the elevated worker session.
/// </summary>
public sealed partial class WorkerServer
{
    private readonly string _pipeName;
    private readonly string _expectedNonce;
    private readonly ElevatedOperationExecutor _executor;
    private readonly int? _expectedParentPid;

    public WorkerServer(
        string pipeName,
        string expectedNonce,
        ElevatedOperationExecutor executor,
        int? expectedParentPid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);

        _pipeName = pipeName;
        _expectedNonce = expectedNonce;
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _expectedParentPid = expectedParentPid;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var pipeSecurity = new PipeSecurity();

                var currentIdentity = WindowsIdentity.GetCurrent();
                if (currentIdentity.User != null)
                {
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        currentIdentity.User,
                        PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                        AccessControlType.Allow));
                }

                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    adminSid,
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));

                return NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity);
            }
            catch
            {
                // Fallback to standard server stream if ACL initialization fails
            }
        }

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var pipeServer = CreatePipeServer(_pipeName);

        using var handshakeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(IpcConstants.HandshakeTimeoutMs));
        using var linkedHandshakeCts = CancellationTokenSource.CreateLinkedTokenSource(handshakeCts.Token, cancellationToken);

        await pipeServer.WaitForConnectionAsync(linkedHandshakeCts.Token).ConfigureAwait(false);

        // 1. Handshake
        var handshakeRequest = await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeRequest>(pipeServer, linkedHandshakeCts.Token).ConfigureAwait(false);

        // Security: verify connected client OS Process ID via kernel32
        uint connectedPid = 0;
        bool pidValid = true;
        if (OperatingSystem.IsWindows() && _expectedParentPid.HasValue)
        {
            pidValid = GetNamedPipeClientProcessId(pipeServer.SafePipeHandle, out connectedPid) &&
                       connectedPid == (uint)_expectedParentPid.Value;
        }

        if (handshakeRequest is null ||
            !string.Equals(handshakeRequest.ClientNonce, _expectedNonce, StringComparison.Ordinal) ||
            !pidValid ||
            (_expectedParentPid.HasValue && handshakeRequest.ClientProcessId != _expectedParentPid.Value))
        {
            var rejectionReason = !pidValid
                ? $"Authentication failed: Unauthorized client process ID (Expected: {_expectedParentPid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}, Connected: {connectedPid.ToString(System.Globalization.CultureInfo.InvariantCulture)})."
                : "Authentication failed: Client credentials mismatch.";

            var rejection = new WorkerHandshakeResponse(
                IsAuthorized: false,
                ServerProcessId: Environment.ProcessId,
                RejectionReason: rejectionReason);

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
