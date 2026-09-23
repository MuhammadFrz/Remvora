using FluentAssertions;
using Remvora.Core.Policies;

namespace Remvora.Core.Tests.Policies;

public sealed class ProtectedPathsPolicyTests
{
    private readonly ProtectedPathsPolicy _policy = new(@"C:\Program Files\Remvora");

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Windows\System32\drivers\etc")]
    [InlineData(@"C:\Windows\SysWOW64")]
    [InlineData(@"C:\Windows\WinSxS")]
    [InlineData(@"C:\Windows\SystemResources")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\bootmgr")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows")]
    [InlineData(@"C:\Program Files\Remvora")]
    [InlineData(@"C:\Program Files\Remvora\Remvora.exe")]
    [InlineData(@"C:\Program Files\Windows Defender")]
    public void IsPathProtected_ForKnownProtectedTargets_ReturnsTrue(string path)
    {
        var isProtected = _policy.IsPathProtected(path, out var reason);

        isProtected.Should().BeTrue();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(@"C:\Program Files\VendorApp\app.exe")]
    [InlineData(@"C:\Users\Username\AppData\Local\VendorApp")]
    [InlineData(@"C:\Program Files (x86)\VendorApp")]
    [InlineData(@"D:\CustomGames\GameName")]
    [InlineData(@"C:\Windows\Temp")]
    [InlineData(@"C:\Windows\Temp\session.tmp")]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download")]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download\patch.cab")]
    public void IsPathProtected_ForLegitimateAppPaths_ReturnsFalse(string path)
    {
        var isProtected = _policy.IsPathProtected(path, out var reason);

        isProtected.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"C:\Program Files\VendorApp\..\..\Windows\System32")]
    [InlineData(@"C:\Windows\System32\..\..\Windows\System32\calc.exe")]
    public void IsPathProtected_WithTraversalTricks_CanonicalizesAndProtects(string traversalPath)
    {
        var isProtected = _policy.IsPathProtected(traversalPath, out var reason);

        isProtected.Should().BeTrue();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(@"HKLM\SYSTEM")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\LanmanServer")]
    [InlineData(@"HKLM\SECURITY")]
    [InlineData(@"HKLM\SAM")]
    [InlineData(@"HKLM\HARDWARE")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion")]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion")]
    [InlineData(@"HKCR")]
    [InlineData(@"HKEY_CLASSES_ROOT\CLSID")]
    [InlineData(@"HKLM\SOFTWARE\Classes")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders")]
    public void IsRegistryKeyProtected_ForSystemRegistryBranches_ReturnsTrue(string registryKey)
    {
        var isProtected = _policy.IsRegistryKeyProtected(registryKey, out var reason);

        isProtected.Should().BeTrue();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(@"HKCU\Software\VendorApp")]
    [InlineData(@"HKLM\Software\VendorApp")]
    [InlineData(@"HKLM\Software\WOW6432Node\VendorApp")]
    public void IsRegistryKeyProtected_ForUserApplicationKeys_ReturnsFalse(string registryKey)
    {
        var isProtected = _policy.IsRegistryKeyProtected(registryKey, out var reason);

        isProtected.Should().BeFalse();
        reason.Should().BeEmpty();
    }
}
