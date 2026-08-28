namespace AncestorsEnhanced.Infrastructure.Tests.Paks;

using System;
using System.IO;
using System.Security.Cryptography;
using AncestorsEnhanced.Infrastructure.Paks;
using Xunit;

public class CdsRegimenComparisonTest
{
    [Fact]
    public void CompareRegimenAssets()
    {
        string pakPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Steam\steamapps\common\Ancestors The Humankind Odyssey\Ancestors\Content\Paks\Ancestors-WindowsNoEditor.pak");
        
        if (!File.Exists(pakPath))
        {
            Console.WriteLine("SKIP: Game PAK not found");
            return;
        }
        
        string humanAiAsset = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/HumanAI/VL01_HumanAI_Shared_CDSRegimen.uasset";
        string playerAsset = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSRegimen.uasset";
        
        byte[] humanAi = PakV5Archive.ReadFile(pakPath, humanAiAsset, 1024 * 1024);
        byte[] player = PakV5Archive.ReadFile(pakPath, playerAsset, 1024 * 1024);
        
        Console.WriteLine($"HumanAI CDSRegimen: {humanAi.Length} bytes, SHA256: {Convert.ToHexString(SHA256.HashData(humanAi))}");
        Console.WriteLine($"Player CDSRegimen:  {player.Length} bytes, SHA256: {Convert.ToHexString(SHA256.HashData(player))}");
        
        if (humanAi.Length != player.Length)
            Console.WriteLine($"\n*** DIFFERENT SIZES: HumanAI={humanAi.Length} vs Player={player.Length} ***\n");
        
        int minLen = Math.Min(humanAi.Length, player.Length);
        int diffCount = 0;
        for (int i = 0; i < minLen; i++)
            if (humanAi[i] != player[i]) diffCount++;
        Console.WriteLine($"Total different bytes: {diffCount} out of {minLen}");
        
        // Check AEC offsets
        Console.WriteLine($"\n=== AEC-patched offsets ===");
        int[] offsets = { 1795, 1968, 2170 };
        float[] stocks = { 24f, 30f, 16f };
        string[] names = { "food", "water", "sleep" };
        for (int i = 0; i < offsets.Length; i++)
        {
            int off = offsets[i];
            if (off + 4 <= humanAi.Length && off + 4 <= player.Length)
            {
                float hVal = BitConverter.ToSingle(humanAi, off);
                float pVal = BitConverter.ToSingle(player, off);
                Console.WriteLine($"  {names[i]} @ offset {off}: HumanAI={hVal} Player={pVal} (stock={stocks[i]})");
            }
        }
        
        // Search Player asset for stock values
        Console.WriteLine($"\n=== Searching Player asset for stock values 24, 30, 16 ===");
        for (int i = 0; i <= player.Length - 4; i++)
        {
            float val = BitConverter.ToSingle(player, i);
            if (Math.Abs(val - 24f) < 0.001f) Console.WriteLine($"  Found 24.0 (food) at offset {i} (0x{i:X4})");
            if (Math.Abs(val - 30f) < 0.001f) Console.WriteLine($"  Found 30.0 (water) at offset {i} (0x{i:X4})");
            if (Math.Abs(val - 16f) < 0.001f) Console.WriteLine($"  Found 16.0 (sleep) at offset {i} (0x{i:X4})");
        }
        
        // Show first 20 differences
        Console.WriteLine($"\n=== First 20 byte differences ===");
        int shown = 0;
        for (int i = 0; i < minLen && shown < 20; i++)
        {
            if (humanAi[i] != player[i])
            {
                float hFloat = (i + 4 <= minLen) ? BitConverter.ToSingle(humanAi, i) : 0;
                float pFloat = (i + 4 <= minLen) ? BitConverter.ToSingle(player, i) : 0;
                Console.WriteLine($"  [{shown}] Offset {i} (0x{i:X4}): H=0x{humanAi[i]:X2} P=0x{player[i]:X2} | Float: H={hFloat} P={pFloat}");
                shown++;
            }
        }
    }
}
