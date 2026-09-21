using System.IO.Pipes;
using FluentAssertions;
using Remvora.Contracts;
using Remvora.Contracts.Handshake;
using Remvora.Contracts.Operations;
using Remvora.Core.Policies;
using Remvora.Elevation;

namespace Remvora.Windows.Tests.Elevation;

public sealed class WorkerIpcTests
{
    [Fact]
    public async Task WorkerServer_WithMatchingNonce_EstablishesSession()
    {
        var pipeName = $"Remvora.TestPipe.{Guid.NewGuid():N}";
        var nonce = Guid.NewGuid().ToString("N");

        var policy = ProtectedPathsPolicy.Default;
        var executor = new ElevatedOperationExecutor(policy);
        var server = new WorkerServer(pipeName, nonce, executor);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        // Connect client
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);

        // Handshake
        var handshakeRequest = new WorkerHandshakeRequest(Environment.ProcessId, nonce);
        await IpcWireProtocol.WriteMessageAsync(client, handshakeRequest, cts.Token);

        var handshakeResponse = await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeResponse>(client, cts.Token);

        handshakeResponse.Should().NotBeNull();
        handshakeResponse!.IsAuthorized.Should().BeTrue();
        handshakeResponse.SessionToken.Should().NotBeNullOrWhiteSpace();

        // Close client to let server exit
        client.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task WorkerServer_WithMismatchedNonce_RejectsHandshake()
    {
        var pipeName = $"Remvora.TestPipe.{Guid.NewGuid():N}";
        var correctNonce = "CorrectNonce_12345";
        var wrongNonce = "WrongNonce_99999";

        var policy = ProtectedPathsPolicy.Default;
        var executor = new ElevatedOperationExecutor(policy);
        var server = new WorkerServer(pipeName, correctNonce, executor);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);

        var handshakeRequest = new WorkerHandshakeRequest(Environment.ProcessId, wrongNonce);
        await IpcWireProtocol.WriteMessageAsync(client, handshakeRequest, cts.Token);

        var handshakeResponse = await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeResponse>(client, cts.Token);

        handshakeResponse.Should().NotBeNull();
        handshakeResponse!.IsAuthorized.Should().BeFalse();
        handshakeResponse.RejectionReason.Should().Contain("mismatch");

        client.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task WorkerServer_ExecuteDeleteFile_DeletesSafeTempFile()
    {
        var pipeName = $"Remvora.TestPipe.{Guid.NewGuid():N}";
        var nonce = Guid.NewGuid().ToString("N");

        var tempFile = Path.Combine(Path.GetTempPath(), $"Remvora_IpcTest_{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempFile, "Temporary test data for worker deletion test");

        try
        {
            var policy = ProtectedPathsPolicy.Default;
            var executor = new ElevatedOperationExecutor(policy);
            var server = new WorkerServer(pipeName, nonce, executor);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var serverTask = Task.Run(() => server.RunAsync(cts.Token));

            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(cts.Token);

            var handshakeReq = new WorkerHandshakeRequest(Environment.ProcessId, nonce);
            await IpcWireProtocol.WriteMessageAsync(client, handshakeReq, cts.Token);
            var handshakeResp = await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeResponse>(client, cts.Token);

            handshakeResp.Should().NotBeNull();
            var sessionToken = handshakeResp!.SessionToken!;

            // Execute DeleteFile
            var deleteReq = new ElevatedOperationRequest(
                correlationId: Guid.NewGuid(),
                sessionToken: sessionToken,
                commandType: ElevatedCommandType.DeleteFile,
                target: tempFile);

            await IpcWireProtocol.WriteMessageAsync(client, deleteReq, cts.Token);
            var deleteResult = await IpcWireProtocol.ReadMessageAsync<ElevatedOperationResult>(client, cts.Token);

            deleteResult.Should().NotBeNull();
            deleteResult!.IsSuccess.Should().BeTrue();
            File.Exists(tempFile).Should().BeFalse();

            client.Dispose();
            await serverTask;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WorkerServer_ExecuteDeleteFile_OnProtectedSystemPath_RefusesDeletion()
    {
        var pipeName = $"Remvora.TestPipe.{Guid.NewGuid():N}";
        var nonce = Guid.NewGuid().ToString("N");

        var policy = ProtectedPathsPolicy.Default;
        var executor = new ElevatedOperationExecutor(policy);
        var server = new WorkerServer(pipeName, nonce, executor);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);

        var handshakeReq = new WorkerHandshakeRequest(Environment.ProcessId, nonce);
        await IpcWireProtocol.WriteMessageAsync(client, handshakeReq, cts.Token);
        var handshakeResp = await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeResponse>(client, cts.Token);

        var sessionToken = handshakeResp!.SessionToken!;

        var protectedTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "kernel32.dll");

        var deleteReq = new ElevatedOperationRequest(
            correlationId: Guid.NewGuid(),
            sessionToken: sessionToken,
            commandType: ElevatedCommandType.DeleteFile,
            target: protectedTarget);

        await IpcWireProtocol.WriteMessageAsync(client, deleteReq, cts.Token);
        var result = await IpcWireProtocol.ReadMessageAsync<ElevatedOperationResult>(client, cts.Token);

        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(8); // ProtectedTarget
        result.ErrorMessage.Should().Contain("blocked");

        client.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task WorkerServer_WithInvalidSessionToken_RejectsOperation()
    {
        var pipeName = $"Remvora.TestPipe.{Guid.NewGuid():N}";
        var nonce = Guid.NewGuid().ToString("N");

        var policy = ProtectedPathsPolicy.Default;
        var executor = new ElevatedOperationExecutor(policy);
        var server = new WorkerServer(pipeName, nonce, executor);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);

        var handshakeReq = new WorkerHandshakeRequest(Environment.ProcessId, nonce);
        await IpcWireProtocol.WriteMessageAsync(client, handshakeReq, cts.Token);
        await IpcWireProtocol.ReadMessageAsync<WorkerHandshakeResponse>(client, cts.Token);

        // Send operation with bogus session token
        var opReq = new ElevatedOperationRequest(
            correlationId: Guid.NewGuid(),
            sessionToken: "BOGUS_SESSION_TOKEN_XYZ",
            commandType: ElevatedCommandType.DeleteFile,
            target: @"C:\dummy\file.txt");

        await IpcWireProtocol.WriteMessageAsync(client, opReq, cts.Token);
        var result = await IpcWireProtocol.ReadMessageAsync<ElevatedOperationResult>(client, cts.Token);

        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(11); // WorkerRejected

        client.Dispose();
        await serverTask;
    }
}
