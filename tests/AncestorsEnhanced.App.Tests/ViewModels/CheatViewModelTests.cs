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

        Assert.Equal(5, viewModel.Slots.Count);
        Assert.Equal("Slot 1", viewModel.Slots[0].Label);
        Assert.Equal(0, viewModel.SelectedSlot.Number);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public async Task MaxNeuronalEnergyDelegatesToService()
    {
        var service = new FakeCheatService();
        var viewModel = new CheatViewModel(service, new IniCheatService(tmp));

        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        Assert.Equal(CheatKind.MaxNeuronalEnergy, service.LastKind);
        Assert.Contains("checkpoint created", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
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


    [Fact]
    public void FreeCameraFailureRevertsWithoutRecursing()
    {
        // Point the user-data directory at an existing FILE so that creating the
        // Config\WindowsNoEditor folder inside it fails with an IOException.
        string asFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aec-file-"+System.Guid.NewGuid().ToString("N"));
        System.IO.File.WriteAllText(asFile, "occupied");
        CheatViewModel viewModel = null!;
        try
        {
            viewModel = new CheatViewModel(new FakeCheatService(), new IniCheatService(asFile));
            viewModel.IsFreeCamEnabled = true;

            // The write failed: the toggle must revert to false *and* stop (no recursion).
            Assert.False(viewModel.IsFreeCamEnabled);
            Assert.Contains("Could not update free camera", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            viewModel?.Dispose();
            if (System.IO.File.Exists(asFile)) { System.IO.File.Delete(asFile); }
        }
    }


    [Fact]
    public async Task RestoreLastCheckpointCallsBackWithSlotAndId()
    {
        var service = new FakeCheatService();
        var restored = new List<(string Slot, string Id)>();
        var viewModel = new CheatViewModel(
            service,
            new IniCheatService(tmp),
            (slot, id) => { restored.Add((slot, id)); return Task.CompletedTask; });
        await viewModel.MaxNeuronalEnergyCommand.ExecuteAsync(null);

        Assert.True(viewModel.CanRestoreLastCheckpoint);
        await viewModel.RestoreLastCheckpointCommand.ExecuteAsync(null);

        var pair = Assert.Single(restored);
        Assert.Equal("0", pair.Slot);
        Assert.Equal("cp-1", pair.Id);
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
