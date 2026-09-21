namespace Remvora.Contracts.Handshake;

/// <summary>
/// Initial connection handshake request sent by the unelevated UI to authenticate with the elevated worker.
/// </summary>
public sealed record WorkerHandshakeRequest(
    int ClientProcessId,
    string ClientNonce,
    int ProtocolVersion = IpcConstants.CurrentProtocolVersion);

/// <summary>
/// Response returned by the elevated worker establishing the authenticated session.
/// </summary>
public sealed record WorkerHandshakeResponse(
    bool IsAuthorized,
    int ServerProcessId,
    string? SessionToken = null,
    string? RejectionReason = null,
    int ProtocolVersion = IpcConstants.CurrentProtocolVersion);
