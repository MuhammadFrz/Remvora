using CommunityToolkit.Mvvm.ComponentModel;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.ViewModels;

public sealed partial class BatchAppItemViewModel : ObservableObject
{
    public ApplicationRecord Model { get; }

    public string DisplayName => Model.DisplayName;
    public string Publisher => string.IsNullOrWhiteSpace(Model.Publisher) ? "Unknown" : Model.Publisher;
    public string FormattedSize => ApplicationItemViewModel.FormatBytes(Model.EstimatedSizeBytes ?? Model.CalculatedSizeBytes ?? 0L);

    [ObservableProperty]
    public partial string Status { get; set; } = "Queued";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }

    [ObservableProperty]
    public partial bool IsSuccess { get; set; }

    public bool IsFailed => IsCompleted && !IsSuccess;

    [ObservableProperty]
    public partial long ReclaimedBytes { get; set; }

    public BatchAppItemViewModel(ApplicationRecord model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }
}
