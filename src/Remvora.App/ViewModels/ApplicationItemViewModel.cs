using CommunityToolkit.Mvvm.ComponentModel;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.ViewModels;

/// <summary>
/// Presentation model representing an installed application in the WinUI UI.
/// </summary>
public sealed partial class ApplicationItemViewModel : ObservableObject
{
    public ApplicationRecord Model { get; }

    public Guid Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? "Unknown Publisher" : Model.Publisher;
    public string DisplayVersion => string.IsNullOrWhiteSpace(Model.DisplayVersion) ? "-" : Model.DisplayVersion;
    public string InstallLocation => string.IsNullOrWhiteSpace(Model.InstallLocation) ? "Location not available" : Model.InstallLocation;

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
                return "?";

            var parts = DisplayName.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
                return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();

            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        }
    }

    public string FormattedSize
    {
        get
        {
            var bytes = Model.EstimatedSizeBytes ?? Model.CalculatedSizeBytes;
            if (bytes == null || bytes <= 0)
                return "-";

            return FormatBytes(bytes.Value);
        }
    }

    public string FormattedInstallDate => Model.InstallDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    public string ScopeDescription => Model.Scope switch
    {
        InstallationScope.PerMachine => "Machine-wide",
        InstallationScope.PerUser => "Current User",
        InstallationScope.SystemProtected => "System Protected",
        _ => "Unknown Scope"
    };

    public string ArchitectureDescription => Model.Architecture switch
    {
        ArchitectureType.X64 => "64-bit (x64)",
        ArchitectureType.X86 => "32-bit (x86)",
        ArchitectureType.Arm64 => "ARM64",
        _ => "Any / Neutral"
    };

    public string InstallerTypeDescription => Model.InstallerType switch
    {
        InstallerType.Msi => "Windows Installer (MSI)",
        InstallerType.InnoSetup => "Inno Setup",
        InstallerType.Nsis => "Nullsoft Installer (NSIS)",
        InstallerType.WiXBurn => "WiX / Burn Bundle",
        InstallerType.StorePackage => "Windows Store / MSIX",
        InstallerType.InstallShield => "InstallShield",
        _ => "Standard Executable"
    };

    public bool IsStoreApp => Model.InstallerType == InstallerType.StorePackage;
    public bool IsMsi => Model.Uninstall.IsMsi;
    public bool CanUninstall => Model.Uninstall.CanUninstall;
    public bool CanQuietUninstall => Model.Uninstall.CanQuietUninstall;

    public ApplicationItemViewModel(ApplicationRecord model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
