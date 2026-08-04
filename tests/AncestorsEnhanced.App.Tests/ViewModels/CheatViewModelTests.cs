using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Core.SaveGames;
using Xunit;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class CheatViewModelTests
{
    [Fact]
    public void ExposesAllSlotsAndDefaultsToSlotZero()
    {
        var viewModel = new CheatViewModel(new FakeCheatService());

        Assert.Equal([0, 1, 2, 3, 4], viewModel.Slots);
        Assert.Equal(0, viewModel.SelectedSlot);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public async Task MaxNeuronalEnergyDelegatesToService()
    {
        var service = new FakeCheatService();
        var viewModel = new CheatViewModel(service);

        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        Assert.Equal(CheatKind.MaxNeuronalEnergy, service.LastKind);
        Assert.Contains("applied", viewModel.StatusMessage);
    }

    [Fact]
    public async Task HealClanDelegatesToService()
    {
        var service = new FakeCheatService();
        var viewModel = new CheatViewModel(service);

        await viewModel.HealClanCommand.ExecuteAsync(null);

        Assert.Equal(CheatKind.HealClan, service.LastKind);
    }

    [Fact]
    public async Task FailedCheatReportsFailureAccent()
    {
        var service = new FakeCheatService { Fail = true };
        var viewModel = new CheatViewModel(service);

        await viewModel.MaxNeedsCommand.ExecuteAsync(null);

        Assert.False(service.LastResult.Succeeded);
        Assert.Equal("#D6BC84", viewModel.StatusAccent);
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