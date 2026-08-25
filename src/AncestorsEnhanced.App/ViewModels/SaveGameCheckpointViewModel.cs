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
    private readonly Func<bool> _canMutate;
    private readonly Action<CheckpointMetadata> _metadataChanged;

    public SaveGameCheckpointViewModel(
        SaveGameCheckpoint checkpoint,
        Func<Task> load,
        Func<Task> delete,
        Func<bool> canRestore,
        Func<bool>? canMutate = null,
        CheckpointMetadata? metadata = null,
        Action<CheckpointMetadata>? metadataChanged = null)
    {
        Id = checkpoint.Id;
        SlotNumber = checkpoint.SlotNumber;
        CreatedLabel = checkpoint.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        SizeLabel = SaveGameSlotViewModel.FormatSize(checkpoint.SizeBytes);
        OriginLabel = FormatOrigin(checkpoint.Origin);
        _load = load;
        _delete = delete;
        _canRestore = canRestore;
        _canMutate = canMutate ?? (() => true);
        _metadataChanged = metadataChanged ?? (_ => { });
        Origin = checkpoint.Origin;
        Title = metadata?.Title ?? "";
        Note = metadata?.Note ?? "";
        IsFavorite = metadata?.IsFavorite ?? false;
    }

    public string Id { get; }

    public string SlotNumber { get; }

    public string CreatedLabel { get; }

    public string SizeLabel { get; }

    public string OriginLabel { get; }

    public string Origin { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial bool IsMetadataEditorVisible { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? CreatedLabel : Title;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public bool CanRestore => _canRestore() && _canMutate();

    public bool CanDelete => _canMutate();

    public bool Matches(string searchText, string originFilter) =>
        (originFilter == "All" || string.Equals(Origin, originFilter, StringComparison.Ordinal)) &&
        (string.IsNullOrWhiteSpace(searchText) ||
         DisplayTitle.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
         Note.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
         OriginLabel.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));

    [ObservableProperty]
    public partial bool IsDeleteConfirmVisible { get; set; }

    [ObservableProperty]
    public partial bool IsRestoreConfirmVisible { get; set; }

    [RelayCommand]
    private void OpenDeleteConfirm()
    {
        if (!CanDelete)
        {
            return;
        }
        IsDeleteConfirmVisible = true;
        IsRestoreConfirmVisible = false;
    }

    [RelayCommand]
    private void ToggleMetadataEditor() => IsMetadataEditorVisible = !IsMetadataEditorVisible;

    partial void OnTitleChanged(string value)
    {
        if (value.Length > 80)
        {
            Title = value[..80];
            return;
        }
        SaveMetadata();
        OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnNoteChanged(string value)
    {
        if (value.Length > 400)
        {
            Note = value[..400];
            return;
        }
        SaveMetadata();
        OnPropertyChanged(nameof(HasNote));
    }

    partial void OnIsFavoriteChanged(bool value) => SaveMetadata();

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
        if (!CanDelete)
        {
            return;
        }
        IsDeleteConfirmVisible = false;
        await _delete();
    }

    public void RefreshMutationAvailability()
    {
        if (!CanDelete)
        {
            IsDeleteConfirmVisible = false;
        }
        RefreshRestoreAvailability();
        OnPropertyChanged(nameof(CanDelete));
    }

    private static string FormatOrigin(string origin) => origin switch
    {
        "Manual" => "Manual backup",
        "AutoBackup" => "Auto-backup",
        "PreRestore" => "Before restore",
        string s when s.StartsWith("Cheat:", StringComparison.Ordinal) => "Legacy modified checkpoint",
        _ => origin,
    };

    private void SaveMetadata() => _metadataChanged(new CheckpointMetadata(
        string.IsNullOrWhiteSpace(Title) ? null : Title.Trim(),
        string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
        IsFavorite));
}
