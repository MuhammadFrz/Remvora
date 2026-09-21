using FluentAssertions;
using Remvora.Core.CommandLine;

namespace Remvora.Core.Tests.CommandLine;

public sealed class CommandLineParserTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", @"C:\Program Files\App\unins000.exe", "/SILENT")]
    [InlineData("\"C:\\App\\uninstall.exe\"", @"C:\App\uninstall.exe", "")]
    [InlineData("C:\\Windows\\system32\\cmd.exe /c calc.exe", @"C:\Windows\system32\cmd.exe", "/c calc.exe")]
    [InlineData("MsiExec.exe /X{12345678-ABCD-1234-ABCD-1234567890AB}", "MsiExec.exe", "/X{12345678-ABCD-1234-ABCD-1234567890AB}")]
    [InlineData("uninstaller.exe", "uninstaller.exe", "")]
    public void Parse_CorrectlySeparatesExecutableAndArguments(string raw, string expectedExe, string expectedArgs)
    {
        var parsed = CommandLineParser.Parse(raw);

        parsed.Should().NotBeNull();
        parsed!.ExecutablePath.Should().Be(expectedExe);
        parsed.Arguments.Should().Be(expectedArgs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithEmptyInput_ReturnsNull(string? raw)
    {
        CommandLineParser.Parse(raw).Should().BeNull();
    }
}
