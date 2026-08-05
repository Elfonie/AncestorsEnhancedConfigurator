namespace AncestorsEnhanced.Core.SaveGames;

public interface ISaveGameManager
{
    SaveGamesSnapshot Inspect();

    SaveGameOperationResult CreateCheckpoint(string slotNumber, string origin = "Manual");

    SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId);

    SaveGameOperationResult DeleteCheckpoint(string slotNumber, string checkpointId);
}
