using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveGameSlotViewModel : ViewModelBase
{
    private const int DefaultVisibleCheckpoints = 2;

    private readonly Func<Task> _create;
    private readonly Func<bool> _canMutate;
    private readonly bool _exists;
    private bool _showAllCheckpoints;

    public SaveGameSlotViewModel(
        SaveGameSlotSnapshot slot,
        Func<Task> create,
        Func<SaveGameCheckpoint, Func<Task>> loadCheckpoint,
        Func<SaveGameCheckpoint, Func<Task>> deleteCheckpoint,
        Func<bool>? canRestore = null,
        Func<bool>? canMutate = null,
        bool showAllCheckpoints = false,
        Func<SaveGameCheckpoint, CheckpointMetadata?>? metadataProvider = null,
        Action<SaveGameCheckpoint, CheckpointMetadata>? metadataChanged = null)
    {
        _exists = slot.Exists;
        SlotNumber = slot.SlotNumber;
        FileName = slot.FileName;
        Title = int.TryParse(slot.SlotNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slotIndex)
            ? string.Create(CultureInfo.CurrentCulture, $"Slot {slotIndex + 1}")
            : slot.SlotNumber;
        Status = slot.ErrorMessage is not null
            ? $"Could not inspect this slot: {slot.ErrorMessage}"
            : slot.Exists
            ? string.Create(CultureInfo.CurrentCulture, $"{FormatSize(slot.SizeBytes ?? 0)} \u00b7 last write {slot.LastWriteTimeUtc?.ToLocalTime():g}")
            : "No save in this slot";
        CheckpointCount = slot.Checkpoints.Count == 1
            ? "1 checkpoint"
            : string.Create(CultureInfo.CurrentCulture, $"{slot.Checkpoints.Count} checkpoints");
        _create = create;
        _canMutate = canMutate ?? (() => true);
        _showAllCheckpoints = showAllCheckpoints;

        Checkpoints = slot.Checkpoints
            .Select(checkpoint => new SaveGameCheckpointViewModel(
                checkpoint,
                loadCheckpoint(checkpoint),
                deleteCheckpoint(checkpoint),
                canRestore ?? (() => true),
                _canMutate,
                metadataProvider?.Invoke(checkpoint),
                metadata => metadataChanged?.Invoke(checkpoint, metadata)))
            .ToArray();
        UpdateVisibleCheckpoints();
    }

    public string SlotNumber { get; }

    public string FileName { get; }

    /// <summary>User-facing one-based slot title, e.g. "Slot 1".</summary>
    public string Title { get; }

    public string Status { get; }

    public string CheckpointCount { get; }

    public IReadOnlyList<SaveGameCheckpointViewModel> Checkpoints { get; }

    public IReadOnlyList<SaveGameCheckpointViewModel> VisibleCheckpoints { get; private set; } = [];

    public int FilteredCheckpointCount { get; private set; }

    public bool HasSave => _exists;

    public bool CanSaveCheckpoint => _exists && _canMutate();

    public bool HasHiddenCheckpoints => VisibleCheckpointSource.Count > DefaultVisibleCheckpoints && !_showAllCheckpoints;

    public bool HasExpandedCheckpoints =>
        _showAllCheckpoints && VisibleCheckpointSource.Count > DefaultVisibleCheckpoints;

    public bool IsShowingAllCheckpoints => _showAllCheckpoints;
    public int HiddenCheckpointCount =>
        Math.Max(0, VisibleCheckpointSource.Count - DefaultVisibleCheckpoints);

    public string ShowAllLabel => HiddenCheckpointCount == 1
        ? "Show 1 older checkpoint"
        : $"Show {HiddenCheckpointCount} older checkpoints";

    [RelayCommand]
    private void ShowOlder() => UpdateVisibleCheckpoints(showAll: true);

    [RelayCommand]
    private void ShowRecent() => UpdateVisibleCheckpoints(showAll: false);

    [RelayCommand]
    private async Task CreateCheckpointAsync() => await _create();

    public void RefreshMutationAvailability()
    {
        OnPropertyChanged(nameof(CanSaveCheckpoint));
        foreach (SaveGameCheckpointViewModel checkpoint in Checkpoints)
        {
            checkpoint.RefreshMutationAvailability();
        }
    }

    public void SetCheckpointFilter(string searchText, string originFilter)
    {
        _filteredCheckpoints = Checkpoints.Where(checkpoint => checkpoint.Matches(searchText, originFilter)).ToArray();
        FilteredCheckpointCount = _filteredCheckpoints.Length;
        UpdateVisibleCheckpoints();
        OnPropertyChanged(nameof(FilteredCheckpointCount));
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024.0):0.##} MB"),
    };

    private void UpdateVisibleCheckpoints(bool? showAll = null)
    {
        _showAllCheckpoints = showAll ?? _showAllCheckpoints;
        IReadOnlyList<SaveGameCheckpointViewModel> candidates = VisibleCheckpointSource;
        VisibleCheckpoints = _showAllCheckpoints || candidates.Count <= DefaultVisibleCheckpoints
            ? candidates
            : candidates.Take(DefaultVisibleCheckpoints).ToArray();
        OnPropertyChanged(nameof(VisibleCheckpoints));
        OnPropertyChanged(nameof(HasHiddenCheckpoints));
        OnPropertyChanged(nameof(HiddenCheckpointCount));
        OnPropertyChanged(nameof(ShowAllLabel));
        OnPropertyChanged(nameof(HasExpandedCheckpoints));
        OnPropertyChanged(nameof(IsShowingAllCheckpoints));
    }

    private SaveGameCheckpointViewModel[]? _filteredCheckpoints;

    private IReadOnlyList<SaveGameCheckpointViewModel> VisibleCheckpointSource =>
        _filteredCheckpoints ?? Checkpoints;
}
