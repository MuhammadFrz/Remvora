using Remvora.Core.Domain.Cleaning;
using Remvora.Core.Domain.Results;

namespace Remvora.Application.Cleaning;

/// <summary>
/// Service responsible for scanning and purging system junk and cache artifacts.
/// </summary>
public interface IJunkCleaner
{
    Task<IReadOnlyList<JunkGroup>> ScanJunkAsync(
        IProgress<JunkScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<long>> CleanJunkAsync(
        IEnumerable<JunkCategory> selectedCategories,
        IProgress<JunkCleanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service responsible for scanning and erasing Windows activity traces and MRUs.
/// </summary>
public interface IPrivacyCleaner
{
    Task<IReadOnlyList<PrivacyItem>> ScanPrivacyTracesAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<int>> CleanPrivacyTracesAsync(
        IEnumerable<string> selectedTraceKeys,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service executing multi-pass cryptographic file destruction.
/// </summary>
public interface ISecureShredder
{
    Task<OperationResult> ShredFilesAsync(
        IEnumerable<string> filePaths,
        ShredderAlgorithm algorithm,
        IProgress<ShredProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
