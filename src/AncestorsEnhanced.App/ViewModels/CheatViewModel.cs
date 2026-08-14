using System.Globalization;
using AncestorsEnhanced.Core.SaveGames;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class CheatViewModel : ViewModelBase, IDisposable
{
    private readonly ISaveGameCheatService _service;
    private readonly Func<string, string, Task<SaveGameOperationResult>>? _restoreCheckpoint;
    private readonly CancellationTokenSource _gameCheckCts = new();
    private bool _started;
    private bool _disposed;
    private Task? _pollTask;
    private string? _lastCheckpointSlot;
    private string? _lastCheckpointId;

    [ObservableProperty]
    public partial IReadOnlyList<CheatSlotChoice> Slots { get; set; } =
        Enumerable.Range(0, 5)
            .Select(number => new CheatSlotChoice(number, $"Slot {number+1}"))
            .ToArray();

    [ObservableProperty]
    public partial CheatSlotChoice? SelectedSlot { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsGameRunning { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusAccent { get; set; } = "#7A877A";

    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    public void UpdateSlotAvailability(IReadOnlyList<SaveGameSlotViewModel> slotViewModels)
    {
        // An empty slot list must clear any previously shown cheat slots, never
        // keep stale entries that no longer correspond to a real save (F136).
        if (slotViewModels.Count == 0)
        {
            Slots = [];
            SelectedSlot = null;
            return;
        }

        Slots = slotViewModels
            .Where(slot => slot.HasSave)
            .Select(slot =>
            {
                int number = int.TryParse(
                    slot.SlotNumber,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed)
                        ? parsed
                        : 0;
                string label = number >= 0
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"Slot {number + 1} \u00b7 {(slot.HasSave ? "saved" : "empty")}")
                    : slot.HasSave ? "saved" : "empty";
                return new CheatSlotChoice(number, label);
            })
            .ToArray();
        if (SelectedSlot is null || !Slots.Any(slot => slot.Number == SelectedSlot.Number))
        {
            SelectedSlot = Slots.Count > 0 ? Slots[0] : null;
        }
        else
        {
            SelectedSlot = Slots.FirstOrDefault(s => s.Number == SelectedSlot.Number) ?? Slots[0];
        }
    }

    public CheatViewModel(ISaveGameCheatService service, Func<string, string, Task<SaveGameOperationResult>>? restoreCheckpoint = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _restoreCheckpoint = restoreCheckpoint;
        SelectedSlot = Slots[0];
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _pollTask = Task.Run(() => PollGameRunningLoopAsync(_gameCheckCts.Token));
    }

    private void SetStatus(string message, string accent)
    {
        StatusMessage = message;
        StatusAccent = accent;
        HasStatus = true;
    }

    public bool CanApply => !IsBusy && !IsGameRunning && SelectedSlot is not null;


    public string SteamCloudWarning { get; } =
        "Steam Cloud: do not choose a conflict option automatically. Compare save dates and sizes first. Local files are the intended version only after a deliberate local checkpoint restore; when unsure, copy the local saves before deciding.";

    [RelayCommand]
    private async Task MaxNeuronalEnergyAsync() =>
        await RunCheatAsync(CheatKind.MaxNeuronalEnergy);

    [RelayCommand]
    private async Task MaxNeedsAsync() =>
        await RunCheatAsync(CheatKind.MaxNeeds);

    [RelayCommand]
    private async Task HealClanAsync() =>
        await RunCheatAsync(CheatKind.HealClan);

    private async Task RunCheatAsync(CheatKind kind)
    {
        if (IsBusy || IsGameRunning)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Applying...";
        StatusAccent = "#FF5A00";
        NotifyState();

        CheatApplyResult result;
        try
        {
            string slot = (SelectedSlot?.Number ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            result = await Task.Run(() => _service.Apply(kind, slot));
        }
        catch (Exception exception)
        {
            SetStatus($"Could not apply cheat: {exception.Message}", "#E04D42");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (result.Succeeded && result.CheckpointId is not null)
        {
            _lastCheckpointSlot = (SelectedSlot?.Number ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _lastCheckpointId = result.CheckpointId;
            StatusMessage = "Cheat checkpoint created. Restore it now to use it in the game.";
            StatusAccent = "#B4D941";
            HasStatus = true;
            OnPropertyChanged(nameof(CanRestoreLastCheckpoint));
        }
        else
        {
            SetStatus(result.Message, result.Succeeded ? "#B4D941" : "#E04D42");
        }
        NotifyState();
    }

    public bool CanRestoreLastCheckpoint => _lastCheckpointId is not null && !IsBusy && !IsGameRunning;

    [RelayCommand]
    private async Task RestoreLastCheckpointAsync()
    {
        if (!CanRestoreLastCheckpoint || _lastCheckpointId is null || _lastCheckpointSlot is null || _restoreCheckpoint is null)
        {
            return;
        }

        IsBusy = true;
        NotifyState();
        try
        {
            SaveGameOperationResult result = await _restoreCheckpoint(_lastCheckpointSlot, _lastCheckpointId);
            if (result.Succeeded)
            {
                SetStatus("Cheat checkpoint restored. Start Ancestors to continue.", "#B4D941");
                _lastCheckpointSlot = null;
                _lastCheckpointId = null;
            }
            else
            {
                SetStatus(result.Message, "#E04D42");
            }
        }
        catch (Exception exception) when (IsExpectedRestoreException(exception))
        {
            SetStatus($"Could not restore: {exception.Message}", "#E04D42");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanRestoreLastCheckpoint));
            NotifyState();
        }
    }    private static bool IsExpectedRestoreException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or NotSupportedException or InvalidDataException;

    partial void OnIsBusyChanged(bool value) => NotifyState();

    partial void OnIsGameRunningChanged(bool value) => NotifyState();

    partial void OnSelectedSlotChanged(CheatSlotChoice? value) => NotifyState();

    private async Task PollGameRunningLoopAsync(CancellationToken token)
    {
        // Rotiert vollständig losgelöst vom UI-Thread; der UI-Thread wird nur bei
        // einer Statusänderung benachrichtigt.
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            bool lastRunning = IsAncestorsRunning();
            await PublishGameRunningAsync(lastRunning);

            while (await timer.WaitForNextTickAsync(token))
            {
                bool running = IsAncestorsRunning();
                if (running != lastRunning)
                {
                    lastRunning = running;
                    await PublishGameRunningAsync(running);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PublishGameRunningAsync(bool running)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsGameRunning = running;
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => IsGameRunning = running);
    }

    private static bool IsAncestorsRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("Ancestors-Win64-Shipping").Length > 0 ||
                   System.Diagnostics.Process.GetProcessesByName("Ancestors").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRestoreLastCheckpoint));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
        _gameCheckCts.Cancel();
        try
        {
            // Kurz warten statt endlos: der Poll-Task marshal ke bleibt an den
            // UI-Dispatcher gebunden und darf einen Test-/Shutdown-Thread nicht blockieren.
            _pollTask?.Wait(TimeSpan.FromMilliseconds(300));
        }
        catch (AggregateException)
        {
        }
        _gameCheckCts.Dispose();
        _pollTask = null;
    }
}


public sealed record CheatSlotChoice(int Number, string Label)
{
    public override string ToString() => Label;
}
