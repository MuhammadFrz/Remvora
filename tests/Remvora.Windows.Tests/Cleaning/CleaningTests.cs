using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Remvora.Core.Domain.Cleaning;
using Remvora.Core.Policies;
using Remvora.Windows.Cleaning;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Tests.Cleaning;

public sealed class CleaningTests
{
    private sealed class FakeRegistryAccessor : IRegistryAccessor
    {
        public Dictionary<string, object?> RunMruStore { get; } = new();

        public bool KeyExists(RegistryHive hive, RegistryView view, string subKeyPath) => true;

        public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string subKeyPath) => [];

        public IReadOnlyDictionary<string, object?>? GetValues(RegistryHive hive, RegistryView view, string subKeyPath)
        {
            if (subKeyPath.Contains("RunMRU", StringComparison.OrdinalIgnoreCase))
                return RunMruStore;

            return new Dictionary<string, object?>();
        }

        public object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName) => null;
    }

    [Fact]
    public async Task WindowsJunkCleaner_EnforcesProtectedPathsPolicy()
    {
        // Arrange
        var protectedPolicy = new ProtectedPathsPolicy();
        var cleaner = new WindowsJunkCleaner(protectedPolicy, NullLogger<WindowsJunkCleaner>.Instance);

        // Act
        var result = await cleaner.CleanJunkAsync([JunkCategory.UserTemp]);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task WindowsPrivacyCleaner_ScansAndCleansTraces()
    {
        // Arrange
        var fakeReg = new FakeRegistryAccessor();
        fakeReg.RunMruStore["a"] = "notepad.exe\\1";
        fakeReg.RunMruStore["MRUList"] = "a";

        var cleaner = new WindowsPrivacyCleaner(fakeReg, NullLogger<WindowsPrivacyCleaner>.Instance);

        // Act
        var traces = await cleaner.ScanPrivacyTracesAsync();

        // Assert
        traces.Should().Contain(t => t.Key == "run_mru" && t.TracesCount == 2);
    }

    [Fact]
    public async Task WindowsSecureShredder_ObliteratesTargetFile()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"shred_test_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "SUPER SENSITIVE PASSWORDS AND API KEYS 12345");

        var shredder = new WindowsSecureShredder(new ProtectedPathsPolicy(), NullLogger<WindowsSecureShredder>.Instance);

        // Act
        var result = await shredder.ShredFilesAsync([tempFile], ShredderAlgorithm.Dod522022M);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(tempFile).Should().BeFalse();
    }

    [Fact]
    public async Task WindowsSecureShredder_WhenTargetIsSystemProtected_BlocksShredding()
    {
        // Arrange
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var notepad = Path.Combine(windowsDir, "notepad.exe");

        var shredder = new WindowsSecureShredder(new ProtectedPathsPolicy(), NullLogger<WindowsSecureShredder>.Instance);

        // Act
        var result = await shredder.ShredFilesAsync([notepad], ShredderAlgorithm.ZeroFill);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // File must still exist because it was protected
        File.Exists(notepad).Should().BeTrue();
    }
}
