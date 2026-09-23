using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Remvora.Application.Elevation;
using Remvora.Contracts;
using Remvora.Contracts.Handshake;
using Remvora.Contracts.Operations;
using Remvora.Core.Domain.Results;

namespace Remvora.Windows.Elevation;

/// <summary>
/// Windows implementation of the elevated worker IPC client using authenticated named pipes.
/// </summary>
public sealed partial class ElevatedWorkerClient : IElevatedWorkerClient
{
    private readonly ILogger<ElevatedWorkerClient> _logger;

    public ElevatedWorkerClient(ILogger<ElevatedWorkerClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationResult<IElevatedSession>> StartSessionAsync(
        bool requestElevation = true,
        CancellationToken cancellationToken = default)
    {
        var workerPath = FindWorkerExecutable();
        if (workerPath is null || !File.Exists(workerPath))
        {
            LogWorkerNotFound(_logger, workerPath ?? "N/A");
            return OperationResult.Failure<IElevatedSession>(
                ErrorCode.WorkerUnavailable,
                $"Remvora.ElevatedWorker.exe could not be located at expected path: {workerPath}");
        }

        var pipeName = $"{IpcConstants.PipePrefix}{Guid.NewGuid():N}";
        var clientNonce = Guid.NewGuid().ToString("N");

        LogLaunchingWorker(_logger, pipeName, requestElevation);

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            Arguments = $"--pipe {pipeName} --nonce {clientNonce} --parent-pid {Environment.ProcessId}",
            UseShellExecute = requestElevation,
            Verb = requestElevation ? "runas" : string.Empty,
            CreateNoWindow = !requestElevation
        };

        Process? workerProcess = null;
        NamedPipeClientStream? pipeClient = null;

        try
        {
            workerProcess = Process.Start(startInfo);
            if (workerProcess is null)
            {
                return OperationResult.Failure<IElevatedSession>(
                    ErrorCode.WorkerUnavailable,
                    "Failed to launch elevated worker process.");
            }

            pipeClient = new NamedPipeClientStream(
                serverName: ".",
                pipeName: pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(IpcConstants.HandshakeTimeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

            await pipeClient.ConnectAsync(linkedCts.Token).ConfigureAwait(false);

            // 1. Send Handshake
            var handshakeRequest = new WorkerHandshakeRequest(
                ClientProcessId: Environment.ProcessId,
                ClientNonce: clientNonce);

            await IpcWireProtocol.WriteMessageAsync(pipeClient, handshakeRequest, linkedCts.Token).ConfigureAwait(false);

            // 2. Await Response
            var handshakeResponse = await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeResponse>(pipeClient, linkedCts.Token).ConfigureAwait(false);
            if (handshakeResponse is null || !handshakeResponse.IsAuthorized || string.IsNullOrWhiteSpace(handshakeResponse.SessionToken))
            {
                var reason = handshakeResponse?.RejectionReason ?? "Unknown rejection";
                LogHandshakeRejected(_logger, reason);
                return OperationResult.Failure<IElevatedSession>(
                    ErrorCode.WorkerRejected,
                    $"Worker handshake rejected: {reason}");
            }

            LogSessionEstablished(_logger, handshakeResponse.ServerProcessId);

            var session = new ElevatedSession(
                pipeStream: pipeClient,
                workerProcess: workerProcess,
                sessionToken: handshakeResponse.SessionToken,
                serverProcessId: handshakeResponse.ServerProcessId);

            // Ownership transferred to session
            pipeClient = null;
            workerProcess = null;

            return OperationResult.Success<IElevatedSession>(session);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure<IElevatedSession>(
                ErrorCode.Timeout,
                "Timed out waiting for elevated worker connection.");
        }
        catch (Exception ex)
        {
            LogWorkerConnectionFailed(_logger, ex);
            return OperationResult.Failure<IElevatedSession>(
                ErrorCode.WorkerUnavailable,
                $"Failed to connect to elevated worker: {ex.Message}");
        }
        finally
        {
            pipeClient?.Dispose();
            if (workerProcess != null && !workerProcess.HasExited)
            {
                try { workerProcess.Kill(); } catch { /* Ignore */ }
                workerProcess.Dispose();
            }
        }
    }

    private static string? FindWorkerExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var direct = Path.Combine(baseDir, "Remvora.ElevatedWorker.exe");
        if (File.Exists(direct))
            return direct;

        // Visual Studio output search
        var parent = Directory.GetParent(baseDir)?.FullName;
        if (parent != null)
        {
            var workerSibling = Path.Combine(parent, "Remvora.Elevation", "Remvora.ElevatedWorker.exe");
            if (File.Exists(workerSibling))
                return workerSibling;
        }

        return direct;
    }

    private sealed class ElevatedSession : IElevatedSession
    {
        private readonly NamedPipeClientStream _pipeStream;
        private readonly Process _workerProcess;
        private bool _isDisposed;

        public int ServerProcessId { get; }
        public string SessionToken { get; }

        public ElevatedSession(
            NamedPipeClientStream pipeStream,
            Process workerProcess,
            string sessionToken,
            int serverProcessId)
        {
            _pipeStream = pipeStream;
            _workerProcess = workerProcess;
            SessionToken = sessionToken;
            ServerProcessId = serverProcessId;
        }

        public async Task<ElevatedOperationResult> ExecuteAsync(
            ElevatedCommandType commandType,
            string target,
            string? secondaryTarget = null,
            bool backupRequested = false,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            var request = new ElevatedOperationRequest(
                correlationId: Guid.NewGuid(),
                sessionToken: SessionToken,
                commandType: commandType,
                target: target,
                secondaryTarget: secondaryTarget,
                backupRequested: backupRequested);

            await IpcWireProtocol.WriteMessageAsync(_pipeStream, request, cancellationToken).ConfigureAwait(false);

            var result = await IpcWireProtocol.ReadMessageAsync<ElevatedOperationResult>(_pipeStream, cancellationToken).ConfigureAwait(false);
            return result ?? new ElevatedOperationResult(request.CorrelationId, IsSuccess: false, ErrorCode: 16, ErrorMessage: "Empty response from worker.");
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            try
            {
                await _pipeStream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disposal errors
            }

            try
            {
                if (!_workerProcess.HasExited)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _workerProcess.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                try { _workerProcess.Kill(); } catch { /* Ignore */ }
            }
            finally
            {
                _workerProcess.Dispose();
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Elevated worker binary not found at: {WorkerPath}")]
    private static partial void LogWorkerNotFound(ILogger logger, string workerPath);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Launching worker pipe: {PipeName} (Elevation: {Elevate})")]
    private static partial void LogLaunchingWorker(ILogger logger, string pipeName, bool elevate);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Elevated worker handshake rejected: {Reason}")]
    private static partial void LogHandshakeRejected(ILogger logger, string reason);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Elevated worker session established with PID {ServerPid}")]
    private static partial void LogSessionEstablished(ILogger logger, int serverPid);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Failed to establish session with elevated worker.")]
    private static partial void LogWorkerConnectionFailed(ILogger logger, Exception ex);
}
