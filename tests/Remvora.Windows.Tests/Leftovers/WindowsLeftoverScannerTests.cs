using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;
using Remvora.Core.Policies;
using Remvora.Windows.Leftovers;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Tests.Leftovers;

public sealed class WindowsLeftoverScannerTests
{
    private sealed class FakeRegistryAccessor : IRegistryAccessor
    {
        public HashSet<(RegistryHive Hive, RegistryView View, string Path)> ExistingKeys { get; } = new();

        public bool KeyExists(RegistryHive hive, RegistryView view, string subKeyPath)
            => ExistingKeys.Contains((hive, view, subKeyPath));

        public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string subKeyPath) => [];
        public IReadOnlyDictionary<string, object?>? GetValues(RegistryHive hive, RegistryView view, string subKeyPath) => null;
        public object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName) => null;
    }

    private static ApplicationRecord CreateApp(string name, string installLocation, string? registryKeyPath = null)
    {
        return new ApplicationRecord(
            id: Guid.NewGuid(),
            displayName: name,
            publisher: "Test Vendor",
            displayVersion: "1.0",
            installDate: null,
            installLocation: installLocation,
            estimatedSizeBytes: 1000,
            calculatedSizeBytes: null,
            scope: InstallationScope.PerMachine,
            architecture: ArchitectureType.X64,
            installerType: InstallerType.InnoSetup,
            identity: new ApplicationIdentity(name, "Test Vendor", "1.0", installLocation, registryKeyPath: registryKeyPath),
            uninstall: new UninstallInfo(@"C:\dummy\unins000.exe"));
    }

    [Fact]
    public async Task FileLeftoverScanner_WhenInstallLocationExists_DiscoversDirectoryAsHighConfidence()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Remvora_ScannerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var policy = new CandidateScoringPolicy(ProtectedPathsPolicy.Default);
            var scanner = new WindowsFileLeftoverScanner(policy, NullLogger<WindowsFileLeftoverScanner>.Instance);

            var app = CreateApp("Test Scanner App", tempDir);
            var candidates = await scanner.ScanAsync(app, ScanOptions.Default);

            candidates.Should().NotBeEmpty();
            var installCandidate = candidates.FirstOrDefault(c => c.Target.Equals(tempDir, StringComparison.OrdinalIgnoreCase));
            installCandidate.Should().NotBeNull();
            installCandidate!.Confidence.Should().Be(CandidateConfidence.High);
            installCandidate.DefaultSelected.Should().BeTrue();
            installCandidate.Kind.Should().Be(CandidateKind.Directory);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RegistryLeftoverScanner_DiscoversVendorAndAppSoftwareKeys()
    {
        var fakeRegistry = new FakeRegistryAccessor();
        fakeRegistry.ExistingKeys.Add((RegistryHive.CurrentUser, RegistryView.Default, @"Software\Test Vendor\Test Scanner App"));
        fakeRegistry.ExistingKeys.Add((RegistryHive.CurrentUser, RegistryView.Default, @"Software\Test Scanner App"));

        var policy = new CandidateScoringPolicy(ProtectedPathsPolicy.Default);
        var scanner = new WindowsRegistryLeftoverScanner(fakeRegistry, policy, NullLogger<WindowsRegistryLeftoverScanner>.Instance);

        var app = CreateApp("Test Scanner App", @"C:\Program Files\Test Scanner App");
        var candidates = await scanner.ScanAsync(app, ScanOptions.Default);

        candidates.Should().NotBeEmpty();
        candidates.Should().Contain(c => c.Target.Contains("Test Vendor\\Test Scanner App"));
        candidates.All(c => c.Kind == CandidateKind.RegistryKey).Should().BeTrue();
    }

    [Fact]
    public async Task ShortcutLeftoverScanner_RunsWithoutErrors()
    {
        var policy = new CandidateScoringPolicy(ProtectedPathsPolicy.Default);
        var scanner = new WindowsShortcutLeftoverScanner(policy, NullLogger<WindowsShortcutLeftoverScanner>.Instance);

        var app = CreateApp("CompletelyFictionalAppName_XYZ_9999", @"C:\Program Files\FictionalApp");
        var candidates = await scanner.ScanAsync(app, ScanOptions.Default);

        candidates.Should().BeEmpty();
    }
}
