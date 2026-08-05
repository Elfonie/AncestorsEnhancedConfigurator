using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveGameSlotViewModel : ViewModelBase
{
    private const int DefaultVisibleCheckpoints = 2;

    private readonly Func<Task> _create;
    private readonly bool _exists;
    private bool _showAllCheckpoints;

    public SaveGameSlotViewModel(
        SaveGameSlotSnapshot slot,
        Func<Task> create,
        Func<SaveGameCheckpoint, Func<Task>> loadCheckpoint,
        Func<SaveGameCheckpoint, Func<Task>> deleteCheckpoint)
    {
        _exists = slot.Exists;
        SlotNumber = slot.SlotNumber;
        FileName = slot.FileName;
        Status = slot.Exists
            ? string.Create(CultureInfo.CurrentCulture, $"{slot.SizeBytes ?? 0} bytes \u00b7 last write {slot.LastWriteTimeUtc?.ToLocalTime():g}")
            : "No save in this slot";
        CheckpointCount = slot.Checkpoints.Count == 1
            ? "1 checkpoint"
            : string.Create(CultureInfo.CurrentCulture, $"{slot.Checkpoints.Count} checkpoints");
        _create = create;

        Checkpoints = slot.Checkpoints
            .Select(checkpoint => new SaveGameCheckpointViewModel(
                checkpoint,
                loadCheckpoint(checkpoint),
                deleteCheckpoint(checkpoint)))
            .ToArray();
        UpdateVisibleCheckpoints();
    }

    public string SlotNumber { get; }

    public string FileName { get; }

    public string Status { get; }

    public string CheckpointCount { get; }

    public IReadOnlyList<SaveGameCheckpointViewModel> Checkpoints { get; }

    public IReadOnlyList<SaveGameCheckpointViewModel> VisibleCheckpoints { get; private set; } = [];

    public bool HasSave => _exists;

    public bool HasCheckpoints => Checkpoints.Count > 0;

    public bool CanSaveCheckpoint => _exists;

    public bool HasHiddenCheckpoints => Checkpoints.Count > DefaultVisibleCheckpoints && !_showAllCheckpoints;

    public bool HasExpandedCheckpoints =>
        _showAllCheckpoints && Checkpoints.Count > DefaultVisibleCheckpoints;
    public int HiddenCheckpointCount =>
        Math.Max(0, Checkpoints.Count - DefaultVisibleCheckpoints);

    public string ShowAllLabel => HiddenCheckpointCount == 1
        ? "Show 1 older checkpoint..."
        : $"Show {HiddenCheckpointCount} older checkpoints...";

    [RelayCommand]
    private void ShowOlder() => UpdateVisibleCheckpoints(showAll: true);

    [RelayCommand]
    private void ShowRecent() => UpdateVisibleCheckpoints(showAll: false);

    [RelayCommand]
    private async Task CreateCheckpointAsync() => await _create();

    private void UpdateVisibleCheckpoints(bool? showAll = null)
    {
        _showAllCheckpoints = showAll ?? _showAllCheckpoints;
        VisibleCheckpoints = _showAllCheckpoints || Checkpoints.Count <= DefaultVisibleCheckpoints
            ? Checkpoints
            : Checkpoints.Take(DefaultVisibleCheckpoints).ToArray();
        OnPropertyChanged(nameof(VisibleCheckpoints));
        OnPropertyChanged(nameof(HasHiddenCheckpoints));
        OnPropertyChanged(nameof(HiddenCheckpointCount));
        OnPropertyChanged(nameof(ShowAllLabel));
        OnPropertyChanged(nameof(HasExpandedCheckpoints));
    }
}