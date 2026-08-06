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
        OriginLabel = FormatOrigin(checkpoint.Origin);
        _load = load;
        _delete = delete;
    }

    public string Id { get; }

    public string SlotNumber { get; }

    public string CreatedLabel { get; }

    public string SizeLabel { get; }

    public string OriginLabel { get; }

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
        IsRestoreConfirmVisible = true;
        IsDeleteConfirmVisible = false;
    }

    [RelayCommand]
    private void CancelRestoreConfirm() => IsRestoreConfirmVisible = false;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsRestoreConfirmVisible = false;
        await _load();
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
        string s when s.StartsWith("Cheat:", StringComparison.Ordinal) => s["Cheat:".Length..] + " cheat",
        _ => origin,
    };
}