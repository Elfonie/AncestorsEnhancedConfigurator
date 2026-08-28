namespace AncestorsEnhanced.Infrastructure.Tests.Paks;

using System;
using System.IO;
using System.Security.Cryptography;
using AncestorsEnhanced.Infrastructure.Paks;
using Xunit;

public class PlayerRegimenStructureTest
{
    [Fact]
    public void DumpPlayerRegimenStructure()
    {
        string pakPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Steam\steamapps\common\Ancestors The Humankind Odyssey\Ancestors\Content\Paks\Ancestors-WindowsNoEditor.pak");

        if (!File.Exists(pakPath)) { Console.WriteLine("SKIP"); return; }

        string playerAsset = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSRegimen.uasset";
        byte[] player = PakV5Archive.ReadFile(pakPath, playerAsset, 1024 * 1024);

        // Dump bytes around each key offset
        int[] offsets = { 2455, 2628, 2657, 2772, 2916, 3089, 3213, 3242, 3271 };
        string[] labels = { "wound-sleep-heal-1", "water-1", "wound-sleep-heal-2", "food", "water-2", "sleep", "energy-rec-1", "energy-rec-2", "energy-rec-3" };

        for (int idx = 0; idx < offsets.Length; idx++)
        {
            int off = offsets[idx];
            Console.WriteLine($"\n=== {labels[idx]} @ offset {off} (0x{off:X4}) ===");
            int start = Math.Max(0, off - 16);
            int end = Math.Min(player.Length, off + 20);
            for (int i = start; i < end; i++)
            {
                float fVal = (i + 4 <= player.Length) ? BitConverter.ToSingle(player, i) : 0;
                string marker = (i == off) ? " >>>" : "    ";
                Console.WriteLine($"{marker} [{i,4}] 0x{i:X4}: 0x{player[i]:X2}  float={fVal}");
            }
        }

        // Search for UE4 property name strings near the offsets
        Console.WriteLine($"\n=== Searching for property name strings in Player asset ===");
        for (int i = 0; i < player.Length - 8; i++)
        {
            int len = BitConverter.ToInt32(player, i);
            if (len > 3 && len < 60 && i + 4 + len <= player.Length)
            {
                bool isAscii = true;
                for (int j = 0; j < len && isAscii; j++)
                {
                    byte b = player[i + 4 + j];
                    if (b < 0x20 || b > 0x7E) isAscii = false;
                }
                if (isAscii)
                {
                    string s = System.Text.Encoding.ASCII.GetString(player, i + 4, len);
                    if (s.Contains("Need") || s.Contains("Food") || s.Contains("Water") || s.Contains("Sleep") ||
                        s.Contains("Regen") || s.Contains("Energy") || s.Contains("Recovery") || s.Contains("Delay") ||
                        s.Contains("Exhaust") || s.Contains("Threshold") || s.Contains("Portion") || s.Contains("Regimen"))
                    {
                        Console.WriteLine($"  Offset {i} (0x{i:X4}): [{s}]");
                    }
                }
            }
        }
    }
}
