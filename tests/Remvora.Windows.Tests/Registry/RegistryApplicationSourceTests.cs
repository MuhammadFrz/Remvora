using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Remvora.Core.Abstractions.Discovery;
using Remvora.Core.Domain.Applications;
using Remvora.Windows.Registry;

namespace Remvora.Windows.Tests.Registry;

public sealed class RegistryApplicationSourceTests
{
    private sealed class FakeRegistryAccessor : IRegistryAccessor
    {
        public Dictionary<(RegistryHive Hive, RegistryView View, string Path), Dictionary<string, object?>> Storage { get; } = new();

        public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string subKeyPath)
        {
            var prefix = subKeyPath.TrimEnd('\\') + "\\";
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in Storage.Keys)
            {
                if (key.Hive == hive && key.View == view && key.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = key.Path[prefix.Length..];
                    var subName = rest.Split('\\')[0];
                    set.Add(subName);
                }
            }

            return set.ToList();
        }

        public IReadOnlyDictionary<string, object?>? GetValues(RegistryHive hive, RegistryView view, string subKeyPath)
        {
            return Storage.TryGetValue((hive, view, subKeyPath), out var val) ? val : null;
        }

        public object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName)
        {
            if (Storage.TryGetValue((hive, view, subKeyPath), out var dict) && dict.TryGetValue(valueName, out var v))
                return v;
            return null;
        }
    }

    [Fact]
    public async Task DiscoverAsync_ParsesApplicationsFrom64BitAnd32BitRegistryViews()
    {
        var fakeRegistry = new FakeRegistryAccessor();

        // 64-bit HKLM app
        fakeRegistry.Storage[(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App64")] =
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DisplayName"] = "App Sixty Four",
                ["Publisher"] = "Vendor 64",
                ["DisplayVersion"] = "1.0.0",
                ["InstallLocation"] = @"C:\Program Files\App64",
                ["UninstallString"] = @"C:\Program Files\App64\unins000.exe",
                ["EstimatedSize"] = 20480, // 20 MB in KB
                ["InstallDate"] = "20260921"
            };

        // 32-bit HKLM app (WOW6432Node)
        fakeRegistry.Storage[(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App32")] =
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DisplayName"] = "App Thirty Two",
                ["Publisher"] = "Vendor 32",
                ["DisplayVersion"] = "2.0.0",
                ["InstallLocation"] = @"C:\Program Files (x86)\App32",
                ["UninstallString"] = @"C:\Program Files (x86)\App32\uninstall.exe",
                ["WindowsInstaller"] = 1
            };

        var source = new RegistryApplicationSource(fakeRegistry, NullLogger<RegistryApplicationSource>.Instance);

        var list = new List<ApplicationRecord>();
        await foreach (var app in source.DiscoverAsync(new DiscoveryFilter(), CancellationToken.None))
        {
            list.Add(app);
        }

        list.Should().HaveCount(2);

        var app64 = list.Single(a => a.DisplayName == "App Sixty Four");
        app64.Architecture.Should().Be(ArchitectureType.X64);
        app64.InstallerType.Should().Be(InstallerType.InnoSetup);
        app64.EstimatedSizeBytes.Should().Be(20480L * 1024L);
        app64.InstallDate.Should().NotBeNull();
        app64.InstallDate!.Value.Year.Should().Be(2026);

        var app32 = list.Single(a => a.DisplayName == "App Thirty Two");
        app32.Architecture.Should().Be(ArchitectureType.X86);
        app32.InstallerType.Should().Be(InstallerType.Msi);
    }

    [Fact]
    public async Task DiscoverAsync_WhenSystemComponent_FiltersOutByDefault()
    {
        var fakeRegistry = new FakeRegistryAccessor();

        fakeRegistry.Storage[(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SysApp")] =
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DisplayName"] = "System Internal Component",
                ["SystemComponent"] = 1,
                ["UninstallString"] = "dummy.exe"
            };

        var source = new RegistryApplicationSource(fakeRegistry, NullLogger<RegistryApplicationSource>.Instance);

        // Default filter: IncludeSystemComponents = false
        var defaultList = new List<ApplicationRecord>();
        await foreach (var app in source.DiscoverAsync(new DiscoveryFilter(), CancellationToken.None))
        {
            defaultList.Add(app);
        }
        defaultList.Should().BeEmpty();

        // Advanced filter: IncludeSystemComponents = true
        var systemList = new List<ApplicationRecord>();
        await foreach (var app in source.DiscoverAsync(new DiscoveryFilter { IncludeSystemComponents = true }, CancellationToken.None))
        {
            systemList.Add(app);
        }
        systemList.Should().HaveCount(1);
        systemList[0].IsSystemComponent.Should().BeTrue();
    }
}
