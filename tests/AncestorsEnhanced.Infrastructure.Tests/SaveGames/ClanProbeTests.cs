using AncestorsEnhanced.Core.SaveGames;
using AncestorsEnhanced.Infrastructure.SaveGames;
using AncestorsEnhanced.Infrastructure.SystemSave;
using Xunit;
using System.Text;
using System.Buffers.Binary;

namespace AncestorsEnhanced.Infrastructure.Tests.SaveGames;

public sealed class ClanProbeTests
{
    [Fact]
    public void Dump()
    {
        byte[] clan = SaveGameCheatProbeAccessor.ClanMember(0.4f);
        SaveGameSchemaNode? root = null;
        try
        {
            root = SaveGameSchemaAnalyzer.Parse(clan);
        }
        catch (System.Exception ex)
        {
            var dbg = new StringBuilder();
            int avail = Math.Min(220, clan.Length);
            for (int i = 0; i < avail; i++)
            {
                dbg.Append(clan[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "clan_hex.txt"), dbg.ToString() + " | ERR " + ex.Message);
            throw;
        }

        var sb = new StringBuilder();
        void Walk(SaveGameSchemaNode n, int depth)
        {
            sb.Append(' ', depth * 2).Append(n.Name).Append(" : ").Append(n.Type ?? "term");
            if (n.StructType is not null) sb.Append(" <").Append(n.StructType).Append('>');
            if (n.ElementType is not null) sb.Append(" [").Append(n.ElementType).Append(']');
            sb.Append(" len=").Append(n.ValueLength).AppendLine();
            foreach (var c in n.Children) Walk(c, depth + 1);
        }
        Walk(root, 0);
        System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "clan_probe.txt"), sb.ToString());
    }
}

static class SaveGameCheatProbeAccessor
{
    public static byte[] ClanMember(float health)
    {
        using var healthM = new MemoryStream();
        healthM.Write(UnrealTaggedProperties.EncodeFloat("Health", health));
        healthM.Write(UnrealTaggedProperties.EncodeTerminator());
        using var cd = new MemoryStream();
        cd.Write(UnrealTaggedProperties.EncodeStruct("CharacterData", "GameCharacterSaveGame", healthM.ToArray()));
        cd.Write(UnrealTaggedProperties.EncodeTerminator());
        using var clan = new MemoryStream();
        clan.Write(UnrealTaggedProperties.EncodeStruct("ClanData", "ClanCharacterSaveData", cd.ToArray()));
        clan.Write(UnrealTaggedProperties.EncodeTerminator());

        using var payload = new MemoryStream();
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, 1);
        payload.Write(count);
        payload.Write(clan.ToArray());

        using var list = new MemoryStream();
        list.Write(EncodeString("CharacterDataList"));
        list.Write(EncodeString("ArrayProperty"));
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(size, payload.Length);
        list.Write(size);
        list.Write(EncodeString("StructProperty"));
        list.WriteByte(0);
        list.Write(payload.ToArray());

        using var pc = new MemoryStream();
        pc.Write(UnrealTaggedProperties.EncodeStruct("PlayerClanData", "ClanData", list.ToArray()));
        pc.Write(UnrealTaggedProperties.EncodeTerminator());
        return pc.ToArray();
    }

    static byte[] EncodeString(string v)
    {
        byte[] t = System.Text.Encoding.UTF8.GetBytes(v);
        byte[] r = new byte[t.Length + 5];
        BinaryPrimitives.WriteInt32LittleEndian(r, t.Length + 1);
        t.CopyTo(r, 4);
        return r;
    }
}