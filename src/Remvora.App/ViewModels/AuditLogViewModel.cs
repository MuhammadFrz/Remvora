using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Remvora.Application.Auditing;
using Remvora.Core.Domain.Auditing;

namespace Remvora.App.ViewModels;

public sealed record AuditEventItemViewModel(
    Guid Id,
    DateTimeOffset Timestamp,
    string FormattedTimestamp,
    AuditCategory Category,
    AuditSeverity Severity,
    string Action,
    string? Target,
    string? Result,
    string? Details);

public sealed partial class AuditLogViewModel : ObservableObject
{
    private readonly IAuditLogRepository _auditRepository;
    private readonly List<AuditEvent> _allEvents = [];

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedCategoryIndex { get; set; } = 0;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    public ObservableCollection<AuditEventItemViewModel> FilteredEvents { get; } = [];

    public AuditLogViewModel(IAuditLogRepository auditRepository)
    {
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
    }

    [RelayCommand]
    public async Task LoadEventsAsync()
    {
        IsLoading = true;
        try
        {
            var events = await _auditRepository.GetEventsAsync(limit: 500);
            _allEvents.Clear();
            _allEvents.AddRange(events);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to load audit history: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryIndexChanged(int value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredEvents.Clear();
        var query = _allEvents.AsEnumerable();

        if (SelectedCategoryIndex == 1) // File & Registry Modifications
        {
            query = query.Where(e => e.Category is AuditCategory.FileModification
                                                or AuditCategory.RegistryModification
                                                or AuditCategory.ServiceModification
                                                or AuditCategory.TaskModification);
        }
        else if (SelectedCategoryIndex == 2) // Transactions & Rollback
        {
            query = query.Where(e => e.Category is AuditCategory.TransactionState
                                                or AuditCategory.Rollback);
        }
        else if (SelectedCategoryIndex == 3) // Elevation & Subprocess
        {
            query = query.Where(e => e.Category is AuditCategory.Elevation
                                                or AuditCategory.UninstallSubprocess);
        }
        else if (SelectedCategoryIndex == 4) // Errors & Security Warnings
        {
            query = query.Where(e => e.Severity >= AuditSeverity.Warning ||
                                     e.Category is AuditCategory.Error or AuditCategory.SecurityWarning);
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var trimmed = SearchQuery.Trim();
            query = query.Where(e =>
                e.Action.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (e.Target?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Details?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Result?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var evt in query)
        {
            FilteredEvents.Add(new AuditEventItemViewModel(
                evt.Id,
                evt.Timestamp,
                evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                evt.Category,
                evt.Severity,
                evt.Action,
                evt.Target,
                evt.Result,
                evt.Details));
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
