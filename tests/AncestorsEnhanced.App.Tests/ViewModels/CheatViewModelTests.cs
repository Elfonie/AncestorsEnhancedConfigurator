using AncestorsEnhanced.App.ViewModels;
using AncestorsEnhanced.Infrastructure.Editing;
using AncestorsEnhanced.Core.SaveGames;
using Xunit;

namespace AncestorsEnhanced.App.Tests.ViewModels;

public sealed class CheatViewModelTests
{
    [Fact]
    public void ExposesAllSlotsAndDefaultsToSlotZero()
    {
        var viewModel = new CheatViewModel(new FakeCheatService(), new IniCheatService(tmp));

        Assert.Equal([0, 1, 2, 3, 4], viewModel.Slots);
        Assert.Equal(0, viewModel.SelectedSlot);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public async Task MaxNeuronalEnergyDelegatesToService()
    {
        var service = new FakeCheatService();
        var viewModel = new CheatViewModel(service, new IniCheatService(tmp));

        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        Assert.Equal(CheatKind.MaxNeuronalEnergy, service.LastKind);
        Assert.Contains("applied", viewModel.StatusMessage);
    }

    [Fact]
    public async Task HealClanDelegatesToService()
    {
        var service = new FakeCheatService();
        var viewModel = new CheatViewModel(service, new IniCheatService(tmp));

        await viewModel.HealClanCommand.ExecuteAsync(null);

        Assert.Equal(CheatKind.HealClan, service.LastKind);
    }

    [Fact]
    public async Task FailedCheatReportsFailureAccent()
    {
        var service = new FakeCheatService { Fail = true };
        var viewModel = new CheatViewModel(service, new IniCheatService(tmp));

        await viewModel.MaxNeedsCommand.ExecuteAsync(null);

        Assert.False(service.LastResult.Succeeded);
        Assert.Equal("#D92316", viewModel.StatusAccent);
    }

    private static readonly string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aec-cheatvm-"+System.Guid.NewGuid().ToString("N"));

    [Fact]
    public void FreeCameraToggleWritesInputIni()
    {
        System.IO.Directory.CreateDirectory(tmp);
        CheatViewModel viewModel = null!;
        try
        {
            viewModel = new CheatViewModel(new FakeCheatService(), new IniCheatService(tmp));
            viewModel.IsFreeCamEnabled = true;

            string inputPath = System.IO.Path.Combine(tmp, "Config", "WindowsNoEditor", "Input.ini");
            Assert.True(System.IO.File.Exists(inputPath));
            Assert.Contains("ConsoleKeys=F10", System.IO.File.ReadAllText(inputPath), StringComparison.Ordinal);
            Assert.Contains("enabled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            viewModel?.Dispose();
            if (System.IO.Directory.Exists(tmp)) { System.IO.Directory.Delete(tmp, true); }
        }
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
