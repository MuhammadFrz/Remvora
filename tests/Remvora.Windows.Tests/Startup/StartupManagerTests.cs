using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Remvora.Application.Elevation;
using Remvora.Application.Transactions;
using Remvora.Core.Domain.Results;
using Remvora.Core.Domain.Startup;
using Remvora.Core.Domain.Transactions;
using Remvora.Windows.Registry;
using Remvora.Windows.Startup;

namespace Remvora.Windows.Tests.Startup;

public sealed class StartupManagerTests
{
    private sealed class FakeRegistryAccessor : IRegistryAccessor
    {
        public Dictionary<(RegistryHive, string), Dictionary<string, object?>> Store { get; } = new();

        public bool KeyExists(RegistryHive hive, RegistryView view, string subKeyPath)
            => Store.ContainsKey((hive, subKeyPath));

        public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string subKeyPath)
            => [];

        public IReadOnlyDictionary<string, object?>? GetValues(RegistryHive hive, RegistryView view, string subKeyPath)
        {
            if (Store.TryGetValue((hive, subKeyPath), out var values))
                return values;

            return null;
        }

        public object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName)
        {
            if (Store.TryGetValue((hive, subKeyPath), out var values) && values.TryGetValue(valueName, out var val))
                return val;

            return null;
        }
    }

    private sealed class FakeBackupService : ITransactionBackupService
    {
        public Task<OperationResult<string>> BackupItemAsync(Guid transactionId, TransactionItemType itemType, string targetLocation, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success("C:\\Backup\\item.bak"));

        public Task<OperationResult> RestoreItemAsync(TransactionItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success());
    }

    private sealed class FakeWorkerClient : IElevatedWorkerClient
    {
        public Task<OperationResult<IElevatedSession>> StartSessionAsync(bool requestElevation = true, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Failure<IElevatedSession>(ErrorCode.WorkerUnavailable, "Not running"));
    }

    [Fact]
    public async Task GetStartupEntriesAsync_DiscoversRegistryRunKeysAndParsesState()
    {
        // Arrange
        var fakeReg = new FakeRegistryAccessor();

        // Add HKCU Run key
        fakeReg.Store[(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run")] = new Dictionary<string, object?>
        {
            ["Discord"] = "\"C:\\Users\\User\\AppData\\Local\\Discord\\app.exe\" --autostart",
            ["Spotify"] = "C:\\Users\\User\\AppData\\Roaming\\Spotify\\Spotify.exe"
        };

        // Mark Spotify as disabled in StartupApproved
        fakeReg.Store[(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")] = new Dictionary<string, object?>
        {
            ["Spotify"] = new byte[] { 0x03, 0x00, 0x00, 0x00 }
        };

        var manager = new WindowsStartupManager(
            fakeReg,
            new FakeBackupService(),
            new FakeWorkerClient(),
            NullLogger<WindowsStartupManager>.Instance);

        // Act
        var entries = await manager.GetStartupEntriesAsync();

        // Assert
        entries.Should().Contain(e => e.Name == "Discord" && e.IsEnabled);
        entries.Should().Contain(e => e.Name == "Spotify" && !e.IsEnabled);

        var discord = entries.First(e => e.Name == "Discord");
        discord.ExecutablePath.Should().Be("C:\\Users\\User\\AppData\\Local\\Discord\\app.exe");
        discord.LocationType.Should().Be(StartupLocationType.RegistryCurrentUser);
    }
}
