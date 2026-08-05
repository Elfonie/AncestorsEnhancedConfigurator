using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveGameCheckpointViewModel : ViewModelBase
{
    private readonly Func<Task> _load;
    private readonly Func<Task> _delete;

    public SaveGameCheckpointViewModel(
        SaveGameCheckpoint checkpoint,
        Func<Task> load,
        Func<Task> delete)
    {
        Id = checkpoint.Id;
        SlotNumber = checkpoint.SlotNumber;
        CreatedLabel = checkpoint.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        SizeLabel = string.Create(CultureInfo.CurrentCulture, $"{checkpoint.SizeBytes} bytes");
        _load = load;
        _delete = delete;
    }

    public string Id { get; }

    public string SlotNumber { get; }

    public string CreatedLabel { get; }

    public string SizeLabel { get; }

    [ObservableProperty]
    public partial bool IsDeleteConfirmVisible { get; set; }

    [RelayCommand]
    private void OpenDeleteConfirm() => IsDeleteConfirmVisible = true;

    [RelayCommand]
    private void CancelDeleteConfirm() => IsDeleteConfirmVisible = false;

    [RelayCommand]
    private async Task LoadAsync() => await _load();

    [RelayCommand]
    private async Task DeleteAsync()
    {
        IsDeleteConfirmVisible = false;
        await _delete();
    }
}