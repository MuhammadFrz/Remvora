using Microsoft.Win32;

namespace Remvora.Windows.Registry;

/// <summary>
/// Abstraction over Windows registry enumeration and value reading to facilitate safe unit testing.
/// </summary>
public interface IRegistryAccessor
{
    bool KeyExists(RegistryHive hive, RegistryView view, string subKeyPath);
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string subKeyPath);
    IReadOnlyDictionary<string, object?>? GetValues(RegistryHive hive, RegistryView view, string subKeyPath);
    object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName);
}

/// <summary>
/// Production registry accessor using Microsoft.Win32.RegistryKey.
/// </summary>
public sealed class WindowsRegistryAccessor : IRegistryAccessor
{
    public bool KeyExists(RegistryHive hive, RegistryView view, string subKeyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            return subKey != null;
        }
        catch
        {
            return false;
        }
    }
    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, RegistryView view, string subKeyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            return subKey?.GetSubKeyNames() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyDictionary<string, object?>? GetValues(RegistryHive hive, RegistryView view, string subKeyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            if (subKey == null)
                return null;

            var valueNames = subKey.GetValueNames();
            var dict = new Dictionary<string, object?>(valueNames.Length, StringComparer.OrdinalIgnoreCase);

            foreach (var name in valueNames)
            {
                dict[name] = subKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }

            return dict;
        }
        catch
        {
            return null;
        }
    }

    public object? GetValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            return subKey?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch
        {
            return null;
        }
    }
}
