using AncestorsEnhanced.Core.SaveGames;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AncestorsEnhanced.App.ViewModels;

public partial class CheatViewModel : ViewModelBase
{
    private readonly ISaveGameCheatService _service;

    [ObservableProperty]
    public partial IReadOnlyList<int> Slots { get; set; } = [0, 1, 2, 3, 4];

    [ObservableProperty]
    public partial int SelectedSlot { get; set; } = 0;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Choose a save slot and a cheat to apply.";

    [ObservableProperty]
    public partial string StatusAccent { get; set; } = "#8FA1AD";

    public CheatViewModel(ISaveGameCheatService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public bool CanApply => !IsBusy;

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
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Applying...";
        StatusAccent = "#78AEE8";
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

        StatusMessage = result.Message;
        StatusAccent = result.Succeeded ? "#62C9A7" : "#D6BC84";
        NotifyState();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApply));
    }

    private void NotifyState() => OnPropertyChanged(nameof(CanApply));
}