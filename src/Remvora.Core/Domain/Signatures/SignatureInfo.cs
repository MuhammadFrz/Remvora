namespace Remvora.Core.Domain.Signatures;

/// <summary>
/// Status of an Authenticode digital signature on an executable file.
/// </summary>
public enum SignatureStatus
{
    Unknown = 0,
    Valid = 1,
    NoSignature = 2,
    Invalid = 3,
    UntrustedRoot = 4,
    Expired = 5
}

/// <summary>
/// Digital signature trust information.
/// </summary>
public sealed record SignatureInfo(
    SignatureStatus Status,
    string? Publisher = null,
    string? Subject = null,
    string? Issuer = null,
    DateTimeOffset? Timestamp = null,
    string? Thumbprint = null,
    string? VerificationError = null);
