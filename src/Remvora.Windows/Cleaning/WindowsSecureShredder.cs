using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Remvora.Application.Cleaning;
using Remvora.Core.Domain.Cleaning;
using Remvora.Core.Domain.Results;
using Remvora.Core.Policies;

namespace Remvora.Windows.Cleaning;

/// <summary>
/// Production cryptographic file shredder implementing DoD 5220.22-M and NIST 800-88
/// multi-pass overwriting, buffer flushing, file truncation, and random renaming.
/// </summary>
public sealed partial class WindowsSecureShredder : ISecureShredder
{
    private const int BufferSize = 64 * 1024; // 64 KB chunks

    private readonly IProtectedPathsPolicy _protectedPathsPolicy;
    private readonly ILogger<WindowsSecureShredder> _logger;

    public WindowsSecureShredder(
        IProtectedPathsPolicy protectedPathsPolicy,
        ILogger<WindowsSecureShredder> logger)
    {
        _protectedPathsPolicy = protectedPathsPolicy ?? throw new ArgumentNullException(nameof(protectedPathsPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationResult> ShredFilesAsync(
        IEnumerable<string> filePaths,
        ShredderAlgorithm algorithm,
        IProgress<ShredProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var fileList = filePaths.Where(File.Exists).ToList();
        if (fileList.Count == 0)
            return OperationResult.Success();

        int totalPasses = GetPassCount(algorithm);
        int fileIndex = 0;

        foreach (var file in fileList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileIndex++;

            if (_protectedPathsPolicy.IsPathProtected(file, out _))
            {
                LogShredBlocked(_logger, file);
                continue;
            }

            try
            {
                await ShredSingleFileAsync(file, algorithm, fileIndex, fileList.Count, totalPasses, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogShredFailed(_logger, ex, file);
            }
        }

        return OperationResult.Success();
    }

    private static int GetPassCount(ShredderAlgorithm algorithm) => algorithm switch
    {
        ShredderAlgorithm.ZeroFill => 1,
        ShredderAlgorithm.Pseudorandom => 1,
        ShredderAlgorithm.Nist80088 => 2,
        ShredderAlgorithm.Dod522022M => 3,
        _ => 1
    };

    private static async Task ShredSingleFileAsync(
        string filePath,
        ShredderAlgorithm algorithm,
        int fileIndex,
        int totalFiles,
        int totalPasses,
        IProgress<ShredProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fi = new FileInfo(filePath)
        {
            Attributes = FileAttributes.Normal
        };

        var length = fi.Length;
        var buffer = new byte[BufferSize];

        for (int pass = 1; pass <= totalPasses; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var passPattern = GetPassPattern(algorithm, pass);

            await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Seek(0, SeekOrigin.Begin);
                long bytesRemaining = length;

                while (bytesRemaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int toWrite = (int)Math.Min(bytesRemaining, BufferSize);
                    FillBuffer(buffer, toWrite, passPattern);

                    await stream.WriteAsync(buffer.AsMemory(0, toWrite), cancellationToken).ConfigureAwait(false);
                    bytesRemaining -= toWrite;
                }

                stream.Flush(flushToDisk: true);
                if (pass == totalPasses)
                {
                    stream.SetLength(0);
                }
            }

            int percent = (int)((((fileIndex - 1) * totalPasses + pass) / (double)(totalFiles * totalPasses)) * 100);
            progress?.Report(new ShredProgress(filePath, fileIndex, totalFiles, pass, totalPasses, percent));
        }

        // Rename to random string before deletion to prevent metadata forensics
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var randomizedPath = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");

        File.Move(filePath, randomizedPath);
        File.Delete(randomizedPath);
    }

    private enum PassPattern
    {
        Zeroes,
        Ones,
        Random
    }

    private static PassPattern GetPassPattern(ShredderAlgorithm algorithm, int passNumber)
    {
        return algorithm switch
        {
            ShredderAlgorithm.ZeroFill => PassPattern.Zeroes,
            ShredderAlgorithm.Pseudorandom => PassPattern.Random,
            ShredderAlgorithm.Nist80088 => passNumber == 1 ? PassPattern.Random : PassPattern.Zeroes,
            ShredderAlgorithm.Dod522022M => passNumber switch
            {
                1 => PassPattern.Zeroes,
                2 => PassPattern.Ones,
                _ => PassPattern.Random
            },
            _ => PassPattern.Zeroes
        };
    }

    private static void FillBuffer(byte[] buffer, int count, PassPattern pattern)
    {
        switch (pattern)
        {
            case PassPattern.Zeroes:
                Array.Clear(buffer, 0, count);
                break;
            case PassPattern.Ones:
                Array.Fill(buffer, (byte)0xFF, 0, count);
                break;
            case PassPattern.Random:
                RandomNumberGenerator.Fill(buffer.AsSpan(0, count));
                break;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Shredding blocked: '{Path}' is protected.")]
    private static partial void LogShredBlocked(ILogger logger, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to shred file '{Path}'")]
    private static partial void LogShredFailed(ILogger logger, Exception ex, string path);
}
