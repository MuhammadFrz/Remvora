namespace Remvora.Core.Policies;

/// <summary>
/// Policy enforcing protection of critical Windows system directories, registry hives, and Remvora assets.
/// </summary>
public interface IProtectedPathsPolicy
{
    bool IsPathProtected(string? rawPath, out string reason);
    bool IsRegistryKeyProtected(string? rawRegistryKey, out string reason);
    string CanonicalizePath(string rawPath);
    string CanonicalizeRegistryKey(string rawRegistryKey);
}

/// <summary>
/// Production implementation of protected path and registry invariants.
/// </summary>
public sealed class ProtectedPathsPolicy : IProtectedPathsPolicy
{
    private readonly HashSet<string> _exactProtectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _protectedPathPrefixes = [];
    private readonly HashSet<string> _exactProtectedRegistryKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _protectedRegistryPrefixes = [];

    public ProtectedPathsPolicy(string? remvoraInstallPath = null)
    {
        InitializeFilesystemProtections(remvoraInstallPath);
        InitializeRegistryProtections();
    }

    private void InitializeFilesystemProtections(string? remvoraInstallPath)
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            AddPrefixProtection(systemRoot, "Windows operating system root directory");
            AddPrefixProtection(Path.Combine(systemRoot, "System32"), "Windows core System32 directory");
            AddPrefixProtection(Path.Combine(systemRoot, "SysWOW64"), "Windows 32-bit SysWOW64 subsystem");
            AddPrefixProtection(Path.Combine(systemRoot, "WinSxS"), "Windows side-by-side component store");
            AddPrefixProtection(Path.Combine(systemRoot, "SystemResources"), "Windows system resources");
            AddPrefixProtection(Path.Combine(systemRoot, "Boot"), "Windows boot configuration");
        }

        var systemDrive = Path.GetPathRoot(systemRoot) ?? @"C:\";
        _exactProtectedPaths.Add(systemDrive.TrimEnd('\\'));
        _exactProtectedPaths.Add(systemDrive);
        AddExactProtection(Path.Combine(systemDrive, "bootmgr"), "System boot manager");
        AddExactProtection(Path.Combine(systemDrive, "BOOTNXT"), "System bootloader");
        AddPrefixProtection(Path.Combine(systemDrive, "Boot"), "System boot directory");
        AddPrefixProtection(Path.Combine(systemDrive, "Recovery"), "Windows recovery environment");
        AddPrefixProtection(Path.Combine(systemDrive, "$Recycle.Bin"), "Windows recycle bin root");

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            AddPrefixProtection(Path.Combine(programData, "Microsoft", "Windows"), "Windows system program data");
            AddPrefixProtection(Path.Combine(programData, "Microsoft", "Crypto"), "System cryptographic keys");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            AddExactProtection(programFiles, "Program Files root folder");
            AddPrefixProtection(Path.Combine(programFiles, "Windows Defender"), "Windows Defender anti-malware");
            AddPrefixProtection(Path.Combine(programFiles, "WindowsApps"), "Windows Apps system repository");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            AddExactProtection(programFilesX86, "Program Files (x86) root folder");
            AddPrefixProtection(Path.Combine(programFilesX86, "Windows Defender"), "Windows Defender anti-malware");
        }

        if (!string.IsNullOrWhiteSpace(remvoraInstallPath))
        {
            AddPrefixProtection(remvoraInstallPath, "Remvora application binaries");
        }
    }

    private void InitializeRegistryProtections()
    {
        AddPrefixRegistryProtection(@"HKEY_LOCAL_MACHINE\SYSTEM", "Windows SYSTEM hardware & service control hive");
        AddPrefixRegistryProtection(@"HKLM\SYSTEM", "Windows SYSTEM hardware & service control hive");
        AddPrefixRegistryProtection(@"HKEY_LOCAL_MACHINE\SECURITY", "Windows SECURITY hive");
        AddPrefixRegistryProtection(@"HKLM\SECURITY", "Windows SECURITY hive");
        AddPrefixRegistryProtection(@"HKEY_LOCAL_MACHINE\SAM", "Windows Security Accounts Manager hive");
        AddPrefixRegistryProtection(@"HKLM\SAM", "Windows Security Accounts Manager hive");
        AddPrefixRegistryProtection(@"HKEY_LOCAL_MACHINE\HARDWARE", "Windows dynamic HARDWARE description tree");
        AddPrefixRegistryProtection(@"HKLM\HARDWARE", "Windows dynamic HARDWARE description tree");
        AddPrefixRegistryProtection(@"HKEY_LOCAL_MACHINE\BCD00000000", "Windows Boot Configuration Data hive");
        AddPrefixRegistryProtection(@"HKLM\BCD00000000", "Windows Boot Configuration Data hive");

        AddExactRegistryProtection(@"HKEY_LOCAL_MACHINE\SOFTWARE", "HKLM Software hive root");
        AddExactRegistryProtection(@"HKLM\SOFTWARE", "HKLM Software hive root");
        AddExactRegistryProtection(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft", "Microsoft software configuration root");
        AddExactRegistryProtection(@"HKLM\SOFTWARE\Microsoft", "Microsoft software configuration root");
        AddExactRegistryProtection(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows", "Windows operating system registry root");
        AddExactRegistryProtection(@"HKLM\SOFTWARE\Microsoft\Windows", "Windows operating system registry root");
        AddExactRegistryProtection(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion", "Windows CurrentVersion configuration root");
        AddExactRegistryProtection(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion", "Windows CurrentVersion configuration root");
        AddPrefixRegistryProtection(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT", "Windows NT core kernel settings");
        AddPrefixRegistryProtection(@"HKLM\SOFTWARE\Microsoft\Windows NT", "Windows NT core kernel settings");

        AddExactRegistryProtection(@"HKEY_CURRENT_USER\Software", "HKCU Software root");
        AddExactRegistryProtection(@"HKCU\Software", "HKCU Software root");
        AddExactRegistryProtection(@"HKEY_CURRENT_USER\Software\Microsoft", "HKCU Microsoft settings root");
        AddExactRegistryProtection(@"HKCU\Software\Microsoft", "HKCU Microsoft settings root");
        AddExactRegistryProtection(@"HKEY_CURRENT_USER\Software\Microsoft\Windows", "HKCU Windows settings root");
        AddExactRegistryProtection(@"HKCU\Software\Microsoft\Windows", "HKCU Windows settings root");
        AddExactRegistryProtection(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion", "HKCU Windows CurrentVersion settings root");
        AddExactRegistryProtection(@"HKCU\Software\Microsoft\Windows\CurrentVersion", "HKCU Windows CurrentVersion settings root");
    }

    private void AddExactProtection(string path, string reason)
    {
        _exactProtectedPaths.Add(CanonicalizePath(path));
    }

    private void AddPrefixProtection(string prefix, string reason)
    {
        _protectedPathPrefixes.Add(CanonicalizePath(prefix));
    }

    private void AddExactRegistryProtection(string key, string reason)
    {
        _exactProtectedRegistryKeys.Add(CanonicalizeRegistryKey(key));
    }

    private void AddPrefixRegistryProtection(string prefix, string reason)
    {
        _protectedRegistryPrefixes.Add(CanonicalizeRegistryKey(prefix));
    }

    public bool IsPathProtected(string? rawPath, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            reason = "Path is null or empty";
            return true;
        }

        string canonical;
        try
        {
            canonical = CanonicalizePath(rawPath);
        }
        catch (Exception ex)
        {
            reason = $"Invalid path format or traversal: {ex.Message}";
            return true;
        }

        if (_exactProtectedPaths.Contains(canonical))
        {
            reason = "Target matches a critical Windows system path exactly";
            return true;
        }

        foreach (var prefix in _protectedPathPrefixes)
        {
            if (canonical.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || canonical.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Target resides within protected location: {prefix}";
                return true;
            }
        }

        return false;
    }

    public bool IsRegistryKeyProtected(string? rawRegistryKey, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(rawRegistryKey))
        {
            reason = "Registry key is null or empty";
            return true;
        }

        var canonical = CanonicalizeRegistryKey(rawRegistryKey);

        if (_exactProtectedRegistryKeys.Contains(canonical))
        {
            reason = "Registry target matches a critical root or system branch exactly";
            return true;
        }

        foreach (var prefix in _protectedRegistryPrefixes)
        {
            if (canonical.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || canonical.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Registry target resides within protected hive/branch: {prefix}";
                return true;
            }
        }

        return false;
    }

    public string CanonicalizePath(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);

        var cleaned = rawPath.Trim().Trim('\"');

        if (cleaned.Contains(".."))
        {
            cleaned = Path.GetFullPath(cleaned);
        }

        return Path.GetFullPath(cleaned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string CanonicalizeRegistryKey(string rawRegistryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRegistryKey);

        var trimmed = rawRegistryKey.Trim().TrimEnd('\\');

        if (trimmed.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            trimmed = "HKLM\\" + trimmed[@"HKEY_LOCAL_MACHINE\".Length..];
        else if (trimmed.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
            trimmed = "HKLM";

        if (trimmed.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            trimmed = "HKCU\\" + trimmed[@"HKEY_CURRENT_USER\".Length..];
        else if (trimmed.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
            trimmed = "HKCU";

        if (trimmed.StartsWith(@"HKEY_CLASSES_ROOT\", StringComparison.OrdinalIgnoreCase))
            trimmed = "HKCR\\" + trimmed[@"HKEY_CLASSES_ROOT\".Length..];
        else if (trimmed.Equals("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase))
            trimmed = "HKCR";

        return trimmed;
    }
}
