using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Remvora.Core.Domain.Monitoring;
using Remvora.Core.Policies;
using Remvora.Windows.Monitoring;
using Xunit;

namespace Remvora.Windows.Tests.Monitoring;

public sealed class InstallationMonitorServiceTests
{
    private readonly ProtectedPathsPolicy _policy = new();

    [Fact]
    public async Task StartAndStopMonitoring_CreatesValidSessionLog()
    {
        var service = new WindowsInstallationMonitorService(_policy, NullLogger<WindowsInstallationMonitorService>.Instance);
        string sessionName = $"Test_Session_{Guid.NewGuid():N}";

        var session = await service.StartMonitoringAsync(sessionName);

        service.IsMonitoringActive.Should().BeTrue();
        session.SessionName.Should().Be(sessionName);
        session.Status.Should().Be(MonitorSessionStatus.Active);

        var completed = await service.StopMonitoringAsync();

        service.IsMonitoringActive.Should().BeFalse();
        completed.Status.Should().Be(MonitorSessionStatus.Completed);
        completed.CompletedAt.Should().NotBeNull();

        // Check saved sessions
        var saved = await service.GetSavedSessionsAsync();
        saved.Should().Contain(s => s.Id == completed.Id);

        // Cleanup
        await service.DeleteSessionAsync(completed.Id);
        var afterDelete = await service.GetSavedSessionsAsync();
        afterDelete.Should().NotContain(s => s.Id == completed.Id);
    }

    [Fact]
    public async Task StartMonitoring_WhenAlreadyActive_ThrowsInvalidOperationException()
    {
        var service = new WindowsInstallationMonitorService(_policy, NullLogger<WindowsInstallationMonitorService>.Instance);
        await service.StartMonitoringAsync("Active Session 1");

        try
        {
            var act = async () => await service.StartMonitoringAsync("Active Session 2");
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            await service.StopMonitoringAsync();
        }
    }
}
