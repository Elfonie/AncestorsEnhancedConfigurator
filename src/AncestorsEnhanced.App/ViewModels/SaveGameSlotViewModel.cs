using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveGameSlotViewModel : ViewModelBase
{
    private readonly Func<Task> _create;

    public SaveGameSlotViewModel(
        SaveGameSlotSnapshot slot,
        Func<Task> create,
        Func<SaveGameCheckpoint, Func<Task>> loadCheckpoint,
        Func<SaveGameCheckpoint, Func<Task>> deleteCheckpoint)
    {
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
    }

    public string SlotNumber { get; }

    public string FileName { get; }

    public string Status { get; }

    public string CheckpointCount { get; }

    public IReadOnlyList<SaveGameCheckpointViewModel> Checkpoints { get; }

    public bool HasCheckpoints => Checkpoints.Count > 0;

    [RelayCommand]
    private async Task CreateCheckpointAsync() => await _create();
}
