using AncestorsEnhanced.Core.Editing;

namespace AncestorsEnhanced.Core.Tests;

public sealed class SettingsOperationResultTests
{
    [Fact]
    public void RollbackStatusesAreNotReportedAsSuccessfulOperations()
    {
        SettingsOperationResult rolledBack = SettingsOperationResult.RolledBack("Restored the previous files.");
        SettingsOperationResult partial = SettingsOperationResult.PartialRollbackRequired(
            "Restore needs manual recovery.",
            "operation.json");

        Assert.False(rolledBack.Succeeded);
        Assert.Equal(SettingsOperationStatus.RolledBack, rolledBack.Status);
        Assert.False(partial.Succeeded);
        Assert.Equal(SettingsOperationStatus.PartialRollbackRequired, partial.Status);
    }

    [Fact]
    public void SuccessfulUserRequestedUndoUsesTheDistinctRevertedStatus()
    {
        SettingsOperationResult result = SettingsOperationResult.Reverted("The original files were restored.");

        Assert.True(result.Succeeded);
        Assert.Equal(SettingsOperationStatus.Reverted, result.Status);
    }
}
