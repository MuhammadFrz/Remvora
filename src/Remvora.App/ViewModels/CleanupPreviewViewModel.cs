using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remvora.Application.Leftovers;
using Remvora.Core.Domain.Applications;
using Remvora.Core.Domain.Leftovers;

namespace Remvora.App.ViewModels;

/// <summary>
/// Presentation model for an individual cleanup candidate in the review UI.
/// </summary>
public sealed partial class CandidateItemViewModel : ObservableObject
{
    public CleanupCandidate Candidate { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Target => Candidate.Target;
    public CandidateKind Kind => Candidate.Kind;
    public string KindName => Candidate.Kind.ToString();
    public string RiskName => Candidate.Risk.ToString();
    public string ConfidenceText => $"{Candidate.Confidence} ({Candidate.ConfidenceScore}%)";
    public string EvidenceSummary => string.Join(" • ", Candidate.EvidenceReasons);
    public bool IsProtected => Candidate.IsProtected;
    public bool CanToggleSelection => !Candidate.IsProtected;

    public string KindGlyph => Candidate.Kind switch
    {
        CandidateKind.File => "\uE7C3",
        CandidateKind.Directory => "\uE8B7",
        CandidateKind.RegistryKey => "\uEA86",
        CandidateKind.RegistryValue => "\uEA86",
        CandidateKind.Service => "\uE9F5",
        CandidateKind.ScheduledTask => "\uE823",
        CandidateKind.StartupEntry => "\uE7B5",
        CandidateKind.Shortcut => "\uE71B",
        _ => "\uE7C3"
    };

    public string SizeFormatted
    {
        get
        {
            if (!Candidate.SizeBytes.HasValue || Candidate.SizeBytes.Value <= 0)
                return "—";

            var bytes = Candidate.SizeBytes.Value;
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

    public CandidateItemViewModel(CleanupCandidate candidate)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        IsSelected = candidate.DefaultSelected && !candidate.IsProtected;
    }
}

/// <summary>
/// ViewModel managing the interactive preview and review of discovered leftovers prior to deletion.
/// </summary>
public sealed partial class CleanupPreviewViewModel : ObservableObject
{
    private readonly ICleanupPlanner _planner;

    [ObservableProperty]
    public partial ApplicationRecord? Application { get; set; }

    [ObservableProperty]
    public partial CleanupPlan? Plan { get; set; }

    public ObservableCollection<CandidateItemViewModel> Candidates { get; } = [];

    [ObservableProperty]
    public partial ObservableCollection<CandidateItemViewModel> FilteredCandidates { get; set; } = [];

    [ObservableProperty]
    public partial string FilterKind { get; set; } = "All";

    [ObservableProperty]
    public partial int TotalCandidatesCount { get; set; }

    [ObservableProperty]
    public partial int SelectedCandidatesCount { get; set; }

    [ObservableProperty]
    public partial string ReclaimableSpaceText { get; set; } = "0 B";

    [ObservableProperty]
    public partial bool RequiresElevation { get; set; }

    public CleanupPreviewViewModel(ICleanupPlanner planner)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    }

    public void LoadPlan(ApplicationRecord application, IReadOnlyList<CleanupCandidate> candidates)
    {
        Application = application;
        Plan = _planner.CreatePlan(application, candidates);

        Candidates.Clear();
        foreach (var candidate in Plan.Candidates)
        {
            var itemVm = new CandidateItemViewModel(candidate);
            itemVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CandidateItemViewModel.IsSelected))
                {
                    UpdateStatistics();
                }
            };
            Candidates.Add(itemVm);
        }

        ApplyFilter();
        UpdateStatistics();
    }

    partial void OnFilterKindChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = Candidates.AsEnumerable();

        query = FilterKind switch
        {
            "Files" => query.Where(c => c.Kind is CandidateKind.File or CandidateKind.Directory),
            "Registry" => query.Where(c => c.Kind is CandidateKind.RegistryKey or CandidateKind.RegistryValue),
            "Services" => query.Where(c => c.Kind is CandidateKind.Service or CandidateKind.ScheduledTask),
            "Shortcuts" => query.Where(c => c.Kind is CandidateKind.Shortcut or CandidateKind.StartupEntry),
            _ => query
        };

        FilteredCandidates = new ObservableCollection<CandidateItemViewModel>(query.ToList());
    }

    private void UpdateStatistics()
    {
        TotalCandidatesCount = Candidates.Count;
        SelectedCandidatesCount = Candidates.Count(c => c.IsSelected);

        var totalBytes = Candidates
            .Where(c => c.IsSelected)
            .Sum(c => c.Candidate.SizeBytes ?? 0L);

        ReclaimableSpaceText = FormatBytes(totalBytes);
        RequiresElevation = Plan?.RequiresElevation ?? false;
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var c in Candidates)
        {
            if (c.CanToggleSelection)
                c.IsSelected = true;
        }
    }

    [RelayCommand]
    public void SelectRecommended()
    {
        foreach (var c in Candidates)
        {
            if (c.CanToggleSelection)
                c.IsSelected = c.Candidate.DefaultSelected;
        }
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var c in Candidates)
        {
            if (c.CanToggleSelection)
                c.IsSelected = false;
        }
    }

    public IReadOnlyList<Guid> GetSelectedCandidateIds() =>
        Candidates.Where(c => c.IsSelected && !c.IsProtected).Select(c => c.Candidate.Id).ToList();

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
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
