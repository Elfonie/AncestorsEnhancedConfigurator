using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SaveManagerViewModel : ViewModelBase, IDisposable
{
    private readonly ISaveGameManager _manager;
    private readonly ISaveGameWatchdog? _watchdog;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "No save games loaded yet.";

    [ObservableProperty]
    public partial string StatusAccent { get; set; } = "#8FA1AD";

    [ObservableProperty]
    public partial IReadOnlyList<SaveGameSlotViewModel> Slots { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsWatchdogEnabled { get; set; }

    private int _cooldownMinutes = 5;

    public int CooldownMinutes
    {
        get => _cooldownMinutes;
        set
        {
            if (SetProperty(ref _cooldownMinutes, value))
            {
                if (_watchdog is not null)
                {
                    _watchdog.Cooldown = TimeSpan.FromMinutes(value);
                }
            }
        }
    }

    public string SteamCloudWarning { get; } =
        "This game uses Steam Cloud. A loaded checkpoint can be overwritten by the cloud on the next start. Pause Steam Cloud or go offline before restoring a save.";

    public SaveManagerViewModel(ISaveGameManager manager, ISaveGameWatchdog? watchdog = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        _watchdog = watchdog;
        if (_watchdog is not null)
        {
            _watchdog.CheckpointCreated += OnWatchdogCheckpointCreated;
        }
    }

    public bool HasSlots => Slots.Count > 0;

    public int[] CooldownChoices { get; } = [5, 10, 20];

    public bool HasNoSlots => !HasSlots;

    public bool CanCreate => !IsBusy;

    public void Refresh(SaveGamesSnapshot snapshot)
    {
        Slots = snapshot.Slots
            .Select(slot => new SaveGameSlotViewModel(
                slot,
                () => RunCreate(slot.SlotNumber),
                checkpoint => () => RunLoad(slot.SlotNumber, checkpoint.Id)))
            .ToArray();
        NotifyState();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Reloading save games...";
        StatusAccent = "#78AEE8";
        NotifyState();
        try
        {
            SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
            Refresh(snapshot);
            StatusMessage = "Save games reloaded.";
            StatusAccent = "#62C9A7";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = $"Could not reload save games: {exception.Message}";
            StatusAccent = "#D6BC84";
        }
        finally
        {
            IsBusy = false;
            NotifyState();
        }
    }

    private async Task RunOperation(Func<SaveGameOperationResult> operation)
    {
        IsBusy = true;
        StatusMessage = "Working...";
        StatusAccent = "#78AEE8";
        NotifyState();
        SaveGameOperationResult result;
        try
        {
            result = await Task.Run(operation);
        }
        finally
        {
            IsBusy = false;
        }

        if (result.Succeeded)
        {
            try
            {
                SaveGamesSnapshot snapshot = await Task.Run(_manager.Inspect);
                Refresh(snapshot);
                StatusMessage = result.Message;
                StatusAccent = "#62C9A7";
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                StatusMessage = $"{result.Message} (checkpoints could not be refreshed: {exception.Message})";
                StatusAccent = "#D6BC84";
            }
        }
        else
        {
            StatusMessage = result.Message;
            StatusAccent = "#D6BC84";
        }

        NotifyState();
    }

    public async Task RunCreate(string slotNumber)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperation(() => _manager.CreateCheckpoint(slotNumber));
    }

    public async Task RunLoad(string slotNumber, string checkpointId)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperation(() => _manager.LoadCheckpoint(slotNumber, checkpointId));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_watchdog is not null)
        {
            _watchdog.CheckpointCreated -= OnWatchdogCheckpointCreated;
            _watchdog.StopWatch();
        }
    }

    partial void OnIsWatchdogEnabledChanged(bool value)
    {
        if (_watchdog is null)
        {
            return;
        }

        if (value)
        {
            _watchdog.Start();
        }
        else
        {
            _watchdog.StopWatch();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreate));
    }

    private async void OnWatchdogCheckpointCreated(object? sender, string slotNumber)
    {
        await RefreshAsync();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSlots));
        OnPropertyChanged(nameof(HasNoSlots));
        OnPropertyChanged(nameof(CanCreate));
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException or FileNotFoundException;
}
