using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.SaveGames;
using Xunit;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class CheatViewModelTests
{
    [Fact]
    public void StartsWithoutInventingUnavailableSlots()
    {
        var viewModel = new CheatViewModel(new FakeCheatService());

        Assert.Empty(viewModel.Slots);
        Assert.Null(viewModel.SelectedSlot);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void UpdateSlotAvailabilityLabelsSlotsOneBased()
    {
        var viewModel = new CheatViewModel(new FakeCheatService());

        viewModel.UpdateSlotAvailability(
        [
            Slot("0", saved: true),
            Slot("4", saved: false),
        ]);

        Assert.Single(viewModel.Slots);
        Assert.Equal("Slot 1 \u00b7 saved", viewModel.Slots[0].Label);
        Assert.All(viewModel.Slots, slot => Assert.DoesNotContain("Slot 01", slot.Label, StringComparison.Ordinal));
        Assert.All(viewModel.Slots, slot => Assert.DoesNotContain("Slot 11", slot.Label, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaxNeuronalEnergyDelegatesToService()
    {
        var service = new FakeCheatService();
        var viewModel = Ready(new CheatViewModel(service));

        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        Assert.Equal(CheatKind.MaxNeuronalEnergy, service.LastKind);
        Assert.Contains("checkpoint created", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealCurrentApeDelegatesToService()
    {
        var service = new FakeCheatService();
        var viewModel = Ready(new CheatViewModel(service));

        await viewModel.HealCurrentApeCommand.ExecuteAsync(null);

        Assert.Equal(CheatKind.HealCurrentApe, service.LastKind);
    }

    [Fact]
    public async Task FailedCheatReportsFailureAccent()
    {
        var service = new FakeCheatService { Fail = true };
        var viewModel = Ready(new CheatViewModel(service));

        await viewModel.MaxNeedsCommand.ExecuteAsync(null);

        Assert.False(service.LastResult.Succeeded);
        Assert.Equal("#E04D42", viewModel.StatusAccent);
    }

    [Fact]
    public async Task RestoreLastCheckpointCallsBackWithSlotAndId()
    {
        var service = new FakeCheatService();
        var restored = new List<(string Slot, string Id)>();
        var viewModel = Ready(new CheatViewModel(
            service,
            (slot, id) => { restored.Add((slot, id)); return Task.FromResult(new SaveGameOperationResult(true, "Loaded.")); }));
        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        Assert.True(viewModel.CanRestoreLastCheckpoint);
        await viewModel.RestoreLastCheckpointCommand.ExecuteAsync(null);

        var pair = Assert.Single(restored);
        Assert.Equal("0", pair.Slot);
        Assert.Equal("cp-1", pair.Id);
    }

    [Fact]
    public async Task CommittedRestoreWarningIsShownAndClearsThePendingCheckpoint()
    {
        const string Warning = "The save was loaded, but its timestamp could not be updated.";
        var viewModel = Ready(new CheatViewModel(
            new FakeCheatService(),
            (slot, id) => Task.FromResult(new SaveGameOperationResult(
                true,
                Warning,
                CommitState: SaveOperationCommitState.CommittedWithWarning))));
        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        await viewModel.RestoreLastCheckpointCommand.ExecuteAsync(null);

        Assert.Equal(Warning, viewModel.StatusMessage);
        Assert.Equal("#D6BC84", viewModel.StatusAccent);
        Assert.False(viewModel.CanRestoreLastCheckpoint);
    }

    [Fact]
    public async Task FailedRestoreWithSafetyCheckpointWarningRemainsRetryable()
    {
        const string Warning = "Restore stopped after creating a safety checkpoint.";
        var viewModel = Ready(new CheatViewModel(
            new FakeCheatService(),
            (slot, id) => Task.FromResult(new SaveGameOperationResult(
                false,
                Warning,
                "safety-checkpoint",
                SaveOperationCommitState.CommittedWithWarning))));
        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        await viewModel.RestoreLastCheckpointCommand.ExecuteAsync(null);

        Assert.Equal(Warning, viewModel.StatusMessage);
        Assert.Equal("#D6BC84", viewModel.StatusAccent);
        Assert.True(viewModel.CanRestoreLastCheckpoint);
    }

    [Fact]
    public async Task RestoreFailureDoesNotClaimSuccess()
    {
        var viewModel = Ready(new CheatViewModel(
            new FakeCheatService(),
            (slot, id) => Task.FromResult(new SaveGameOperationResult(false, "Close Ancestors before restoring."))));
        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        await viewModel.RestoreLastCheckpointCommand.ExecuteAsync(null);

        Assert.Contains("Close Ancestors", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("#E04D42", viewModel.StatusAccent);
    }

    private static SaveGameSlotViewModel Slot(string slotNumber, bool saved)
    {
        var snapshot = new SaveGameSlotSnapshot(
            slotNumber,
            $"Savegame{slotNumber}.sav",
            $"path-{slotNumber}",
            Exists: saved,
            null,
            null,
            []);
        return new SaveGameSlotViewModel(
            snapshot,
            () => Task.CompletedTask,
            _ => () => Task.CompletedTask,
            _ => () => Task.CompletedTask);
    }

    private static CheatViewModel Ready(CheatViewModel viewModel)
    {
        viewModel.UpdateSlotAvailability([Slot("0", saved: true)]);
        return viewModel;
    }

    private sealed class FakeCheatService : ISaveGameCheatService
    {
        public CheatKind? LastKind { get; private set; }

        public CheatApplyResult LastResult { get; private set; } =
            new(true, "placeholder");

        public bool Fail { get; set; }

        public CheatApplyResult Apply(CheatKind kind, string slotNumber)
        {
            LastKind = kind;
            LastResult = Fail
                ? new CheatApplyResult(false, "No matching fields were found; nothing was changed.")
                : new CheatApplyResult(true, $"{kind} applied.", "cp-1");
            return LastResult;
        }
    }
}
