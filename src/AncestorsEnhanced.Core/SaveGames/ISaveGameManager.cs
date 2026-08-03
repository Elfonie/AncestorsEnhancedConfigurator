namespace AncestorsEnhanced.Core.SaveGames;

public interface ISaveGameManager
{
    SaveGamesSnapshot Inspect();

    SaveGameOperationResult CreateCheckpoint(string slotNumber);

    SaveGameOperationResult LoadCheckpoint(string slotNumber, string checkpointId);
}
