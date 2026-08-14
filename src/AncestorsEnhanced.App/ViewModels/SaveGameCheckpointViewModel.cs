using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveGameCheckpointViewModel : ViewModelBase
{
    private readonly Func<Task> _load;
    private readonly Func<Task> _delete;
    private readonly Func<bool> _canRestore;

    public SaveGameCheckpointViewModel(
        SaveGameCheckpoint checkpoint,
        Func<Task> load,
        Func<Task> delete,
        Func<bool> canRestore)
    {
        Id = checkpoint.Id;
        SlotNumber = checkpoint.SlotNumber;
        CreatedLabel = checkpoint.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        SizeLabel = SaveGameSlotViewModel.FormatSize(checkpoint.SizeBytes);
        OriginLabel = FormatOrigin(checkpoint.Origin);
        _load = load;
        _delete = delete;
        _canRestore = canRestore;
    }

    public string Id { get; }

    public string SlotNumber { get; }

    public string CreatedLabel { get; }

    public string SizeLabel { get; }

    public string OriginLabel { get; }

    public bool CanRestore => _canRestore();

    [ObservableProperty]
    public partial bool IsDeleteConfirmVisible { get; set; }

    [ObservableProperty]
    public partial bool IsRestoreConfirmVisible { get; set; }

    [RelayCommand]
    private void OpenDeleteConfirm()
    {
        IsDeleteConfirmVisible = true;
        IsRestoreConfirmVisible = false;
    }

    [RelayCommand]
    private void CancelDeleteConfirm() => IsDeleteConfirmVisible = false;

    [RelayCommand]
    private void OpenRestoreConfirm()
    {
        if (!CanRestore)
        {
            return;
        }

        IsRestoreConfirmVisible = true;
        IsDeleteConfirmVisible = false;
    }

    [RelayCommand]
    private void CancelRestoreConfirm() => IsRestoreConfirmVisible = false;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!CanRestore)
        {
            return;
        }

        IsRestoreConfirmVisible = false;
        await _load();
    }

    public void RefreshRestoreAvailability()
    {
        if (!CanRestore)
        {
            IsRestoreConfirmVisible = false;
        }

        OnPropertyChanged(nameof(CanRestore));
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        IsDeleteConfirmVisible = false;
        await _delete();
    }

    private static string FormatOrigin(string origin) => origin switch
    {
        "Manual" => "Manual backup",
        "AutoBackup" => "Auto-backup",
        "PreRestore" => "Before restore",
        "Cheat:HealClan" => "Heal Current Ape cheat",
        string s when s.StartsWith("Cheat:", StringComparison.Ordinal) => s["Cheat:".Length..] + " cheat",
        _ => origin,
    };
}
