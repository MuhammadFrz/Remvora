using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Application.Tools;
using Remvora.Core.Domain.Results;
using Remvora.Windows.Tools;
using Xunit;

namespace Remvora.Windows.Tests.Tools;

public sealed class WindowsToolsServiceTests
{
    [Fact]
    public void GetAllTools_ReturnsCuratedListOfToolsWithValidProperties()
    {
        var service = new WindowsToolsService(NullLogger<WindowsToolsService>.Instance);

        var tools = service.GetAllTools();

        tools.Should().NotBeNullOrEmpty();
        tools.Count.Should().BeGreaterThanOrEqualTo(10);

        foreach (var tool in tools)
        {
            tool.Id.Should().NotBeNullOrWhiteSpace();
            tool.Name.Should().NotBeNullOrWhiteSpace();
            tool.Description.Should().NotBeNullOrWhiteSpace();
            tool.Executable.Should().NotBeNullOrWhiteSpace();
            tool.IconGlyph.Should().NotBeNullOrWhiteSpace();
        }

        // Verify key tools are present
        tools.Should().Contain(t => t.Id == "regedit");
        tools.Should().Contain(t => t.Id == "services");
        tools.Should().Contain(t => t.Id == "taskschd");
        tools.Should().Contain(t => t.Id == "cleanmgr");
        tools.Should().Contain(t => t.Id == "resmon");
    }

    [Fact]
    public void LaunchTool_WithNonExistentExecutable_ReturnsFailureResultGracefully()
    {
        var service = new WindowsToolsService(NullLogger<WindowsToolsService>.Instance);
        var nonExistentTool = new WindowsToolItem(
            "fake_tool",
            "Fake Tool",
            "Does not exist",
            ToolCategory.SystemAdministration,
            "non_existent_tool_binary_xyz123.exe"
        );

        var result = service.LaunchTool(nonExistentTool, runAsAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(ErrorCode.ExecutionFailed);
    }
}
