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

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Media.ImageSource? IconSource { get; set; }

    [ObservableProperty]
    public partial bool HasIcon { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public event Action<ApplicationItemViewModel>? SelectionChanged;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this);

    public ApplicationItemViewModel(ApplicationRecord model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _ = LoadIconAsync();
    }

    public async Task LoadIconAsync()
    {
        if (HasIcon || IconSource != null)
            return;

        try
        {
            var path = ResolveIconPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
                IconSource = bitmap;
                HasIcon = true;
                return;
            }

            var storageFile = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var thumbnail = await storageFile.GetThumbnailAsync(global::Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 48);
            if (thumbnail != null)
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(thumbnail);
                IconSource = bitmap;
                HasIcon = true;
            }
        }
        catch
        {
            HasIcon = false;
        }
    }

    private string? ResolveIconPath()
    {
        // 1. Check DisplayIcon
        if (!string.IsNullOrWhiteSpace(Model.DisplayIcon))
        {
            var raw = Model.DisplayIcon.Trim();
            if (raw.StartsWith('\"'))
            {
                var close = raw.IndexOf('\"', 1);
                if (close > 0)
                    raw = raw.Substring(1, close - 1);
            }
            else if (raw.Contains(','))
            {
                raw = raw.Split(',')[0].Trim('\"', ' ');
            }

            raw = Environment.ExpandEnvironmentVariables(raw);
            if (File.Exists(raw))
                return raw;
        }

        // 2. Check InstallLocation
        if (!string.IsNullOrWhiteSpace(Model.InstallLocation) && Directory.Exists(Model.InstallLocation))
        {
            try
            {
                var files = Directory.GetFiles(Model.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    return files[0];
            }
            catch { }
        }

        // 3. Check UninstallString
        if (!string.IsNullOrWhiteSpace(Model.Uninstall.UninstallString))
        {
            var exe = Remvora.Core.CommandLine.CommandLineParser.ExtractExecutablePath(Model.Uninstall.UninstallString);
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                return exe;
        }

        return null;
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
