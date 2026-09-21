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

        UpdateStatistics();
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
    private void SelectAll()
    {
        foreach (var c in Candidates)
        {
            if (c.CanToggleSelection)
                c.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var c in Candidates)
        {
            if (c.CanToggleSelection)
                c.IsSelected = c.Candidate.DefaultSelected;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var c in Candidates)
        {
            if (c.CanToggleSelection)
                c.IsSelected = false;
        }
    }

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
