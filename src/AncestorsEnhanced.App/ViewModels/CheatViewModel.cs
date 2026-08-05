using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.Editing;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class CheatViewModel : ViewModelBase, IDisposable
{
    private readonly ISaveGameCheatService _service;
    private readonly IniCheatService _iniCheat;
    private readonly CancellationTokenSource _gameCheckCts = new();

    [ObservableProperty]
    public partial IReadOnlyList<int> Slots { get; set; } = [0, 1, 2, 3, 4];

    [ObservableProperty]
    public partial int SelectedSlot { get; set; } = 0;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsGameRunning { get; set; }

    [ObservableProperty]
    public partial bool IsFreeCamEnabled { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusAccent { get; set; } = "#7A877A";

    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    public CheatViewModel(ISaveGameCheatService service, IniCheatService iniCheat)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(iniCheat);
        _service = service;
        _iniCheat = iniCheat;

        _ = Task.Run(() => PollGameRunningLoopAsync(_gameCheckCts.Token));
    }


    private void SetStatus(string message, string accent)
    {
        StatusMessage = message;
        StatusAccent = accent;
        HasStatus = true;
    }

    public bool CanApply => !IsBusy && !IsGameRunning;

    public string SteamCloudWarning { get; } =
        "Steam Cloud Tip: If Steam shows a Cloud Conflict on launch, select Upload to Steam Cloud (Local files) to keep the applied save or cheat checkpoint.";

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
            string slot = SelectedSlot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result = await Task.Run(() => _service.Apply(kind, slot));
        }
        finally
        {
            IsBusy = false;
        }

        SetStatus(result.Message, result.Succeeded ? "#B4D941" : "#D92316");
        NotifyState();
    }

    partial void OnIsFreeCamEnabledChanged(bool value)
    {
        SetStatus("Updating free camera setting...", "#FF5A00");
        try
        {
            _iniCheat.SetFreeCamera(value);
            SetStatus(value ? "Free camera (F10) enabled. Press F10 in-game to toggle." : "Free camera disabled.", "#B4D941");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or NotSupportedException)
        {
            SetStatus($"Could not update free camera: {exception.Message}", "#D92316");
            IsFreeCamEnabled = !value;
        }
    }

    partial void OnIsBusyChanged(bool value) => NotifyState();

    partial void OnIsGameRunningChanged(bool value) => NotifyState();

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

    private void NotifyState() => OnPropertyChanged(nameof(CanApply));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _gameCheckCts.Cancel();
    }
}