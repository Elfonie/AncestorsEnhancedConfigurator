namespace AncestorsEnhanced.Infrastructure.Tests.Paks;

using System;
using System.IO;
using System.Security.Cryptography;
using AncestorsEnhanced.Infrastructure.Paks;
using Xunit;

public class PlayerRegimenAnalysisTest
{
    [Fact]
    public void AnalyzePlayerRegimenStructure()
    {
        string pakPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Steam\steamapps\common\Ancestors The Humankind Odyssey\Ancestors\Content\Paks\Ancestors-WindowsNoEditor.pak");
        
        if (!File.Exists(pakPath)) { Console.WriteLine("SKIP"); return; }
        
        string playerAsset = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSRegimen.uasset";
        byte[] player = PakV5Archive.ReadFile(pakPath, playerAsset, 1024 * 1024);
        
        Console.WriteLine($"Player CDSRegimen: {player.Length} bytes");
        Console.WriteLine($"SHA256: {Convert.ToHexString(SHA256.HashData(player))}");
        
        // Find ALL float values that match stock values
        Console.WriteLine($"\n=== All float values in Player asset ===");
        for (int i = 0; i <= player.Length - 4; i++)
        {
            float val = BitConverter.ToSingle(player, i);
            // Check for interesting values: 24, 30, 16, 1.5, 0.5, 0.15, 1.0, 0.025, 0.05, 0.01, 0.02
            if (Math.Abs(val - 24f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (food stock=24)");
            if (Math.Abs(val - 30f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (water stock=30)");
            if (Math.Abs(val - 16f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (sleep stock=16)");
            if (Math.Abs(val - 1.5f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (rest-delay stock=1.5)");
            if (Math.Abs(val - 0.5f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (exhaustion-threshold stock=0.5)");
            if (Math.Abs(val - 0.15f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (exhaustion-penalty stock=0.15)");
            if (Math.Abs(val - 1f) < 0.001f && i > 100) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (energy-recovery stock=1.0)");
            if (Math.Abs(val - 10f) < 0.001f && i > 100) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (wound-sleep-healing stock=10)");
            if (Math.Abs(val - 480f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (wound-recovery-duration stock=480)");
        }
        
        // Also check HumanAI for comparison
        string humanAiAsset = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/HumanAI/VL01_HumanAI_Shared_CDSRegimen.uasset";
        byte[] humanAi = PakV5Archive.ReadFile(pakPath, humanAiAsset, 1024 * 1024);
        
        Console.WriteLine($"\n=== All float values in HumanAI asset ===");
        for (int i = 0; i <= humanAi.Length - 4; i++)
        {
            float val = BitConverter.ToSingle(humanAi, i);
            if (Math.Abs(val - 24f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (food stock=24)");
            if (Math.Abs(val - 30f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (water stock=30)");
            if (Math.Abs(val - 16f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (sleep stock=16)");
            if (Math.Abs(val - 1.5f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (rest-delay stock=1.5)");
            if (Math.Abs(val - 0.5f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (exhaustion-threshold stock=0.5)");
            if (Math.Abs(val - 0.15f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (exhaustion-penalty stock=0.15)");
            if (Math.Abs(val - 1f) < 0.001f && i > 100) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (energy-recovery stock=1.0)");
            if (Math.Abs(val - 10f) < 0.001f && i > 100) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (wound-sleep-healing stock=10)");
            if (Math.Abs(val - 480f) < 0.001f) Console.WriteLine($"  Offset {i} (0x{i:X4}): {val} (wound-recovery-duration stock=480)");
        }
    }
}
