using System.Text.Json;
using FluentAssertions;
using Remvora.Contracts;
using Remvora.Contracts.Handshake;
using Remvora.Contracts.Operations;

namespace Remvora.Contracts.Tests;

public sealed class SerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void WorkerHandshakeRequest_RoundtripsThroughJson()
    {
        var request = new WorkerHandshakeRequest(1234, "secure-nonce-guid-12345", IpcConstants.CurrentProtocolVersion);

        var json = JsonSerializer.Serialize(request, Options);
        var deserialized = JsonSerializer.Deserialize<WorkerHandshakeRequest>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.ClientProcessId.Should().Be(1234);
        deserialized.ClientNonce.Should().Be("secure-nonce-guid-12345");
        deserialized.ProtocolVersion.Should().Be(IpcConstants.CurrentProtocolVersion);
    }

    [Fact]
    public void ElevatedOperationRequest_RoundtripsThroughJson()
    {
        var correlationId = Guid.NewGuid();
        var request = new ElevatedOperationRequest(
            correlationId,
            "valid-session-token",
            ElevatedCommandType.DeleteFile,
            @"C:\Program Files\OldApp\leftover.dll",
            secondaryTarget: null,
            backupRequested: true,
            expectedOriginalState: "file_exists");

        var json = JsonSerializer.Serialize(request, Options);
        var deserialized = JsonSerializer.Deserialize<ElevatedOperationRequest>(json, Options);

        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.Should().Be(correlationId);
        deserialized.CommandType.Should().Be(ElevatedCommandType.DeleteFile);
        deserialized.Target.Should().Be(@"C:\Program Files\OldApp\leftover.dll");
        deserialized.BackupRequested.Should().BeTrue();
    }
}
