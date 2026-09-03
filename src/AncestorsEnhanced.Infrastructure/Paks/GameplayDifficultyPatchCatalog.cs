using System.Buffers.Binary;

namespace AncestorsEnhanced.Infrastructure.Paks;

/// <summary>Exact Steam build 5495393 definitions, derived from the versioned research ledger.</summary>
internal static class GameplayDifficultyPatchCatalog
{
    private const string Pak = "Ancestors-WindowsNoEditor.pak";
    private const string Regimen = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/HumanAI/VL01_HumanAI_Shared_CDSRegimen.uasset";
    private const string PlayerRegimen = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSRegimen.uasset";
    private const string Damage = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSDamage.uasset";
    private const string Bleed = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSBleed.uasset";
    private const string Poison = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSVenomPoisoning.uasset";
    private const string Vitality = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSVitality.uasset";
    private const string Wounds = "Ancestors/Content/Prod/Maps/Volume01/Common/Character/Controller/Player/VL01_Player_Shared_CDSWounds.uasset";

    public static IReadOnlyList<GameplayAssetPatch> Create(
        int foodPercent,
        int waterPercent,
        int sleepPercent,
        int fallPercent,
        int bleedingPercent,
        int poisonPercent,
        int energyRecoveryPercent,
        int woundSleepHealingPercent,
        int woundStaminaPenaltyPercent,
        int poisonRecoveryPercent,
        int restDelayPercent,
        int exhaustionThresholdPercent,
        int exhaustionPenaltyPercent,
        int woundRecoveryDurationPercent,
        int poisonStaminaPenaltyPercent,
        bool includeClan = false)
    {
        var patches = new List<GameplayAssetPatch>
        {
            Float("food", PlayerRegimen, "7A8C4AD018ECCB2652A82ADFEB7E1AD333FF0F0B30F39F01968FC5CE27CFB42D", 3403, 2772, 24f, foodPercent),
            Float("water", PlayerRegimen, "7A8C4AD018ECCB2652A82ADFEB7E1AD333FF0F0B30F39F01968FC5CE27CFB42D", 3403, 2916, 30f, waterPercent),
            Float("sleep", PlayerRegimen, "7A8C4AD018ECCB2652A82ADFEB7E1AD333FF0F0B30F39F01968FC5CE27CFB42D", 3403, 3089, 16f, sleepPercent),
            Float("minor-fall", Damage, "F980291295F36145ECC069D54F3FB4F171ECAD42F514F13EEFFD00F9002B6D26", 1723, 1621, .025f, fallPercent),
            Float("major-fall", Damage, "F980291295F36145ECC069D54F3FB4F171ECAD42F514F13EEFFD00F9002B6D26", 1723, 1699, .05f, fallPercent),
            Float("minor-bleed", Bleed, "F99BEBCD2DD078781B44BAAD90CC2A86EC44E3DD894A7320C64CAE0D213FF3F1", 2754, 2466, .01f, bleedingPercent),
            Float("major-bleed", Bleed, "F99BEBCD2DD078781B44BAAD90CC2A86EC44E3DD894A7320C64CAE0D213FF3F1", 2754, 2610, .02f, bleedingPercent),
            Float("minor-poison", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2447, .01f, poisonPercent),
            Float("major-poison", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2678, .02f, poisonPercent),
            Float("energy-recovery", Vitality, "1CECDF167848D55AF1A12082E7655C5290589FE7F81DA84810A5CB9DAFB83F26", 2045, 1887, 1f, energyRecoveryPercent),
            Float("minor-wound-sleep-healing", Wounds, "94E49128B08ED6F2F2A82EB7AD956ED4395EF9D946E0B0E7C4680BBE7090435E", 2717, 2404, 10f, woundSleepHealingPercent),
            Float("major-wound-sleep-healing", Wounds, "94E49128B08ED6F2F2A82EB7AD956ED4395EF9D946E0B0E7C4680BBE7090435E", 2717, 2606, 10f, woundSleepHealingPercent),
            Float("minor-wound-stamina-penalty", Wounds, "94E49128B08ED6F2F2A82EB7AD956ED4395EF9D946E0B0E7C4680BBE7090435E", 2717, 2375, .15f, woundStaminaPenaltyPercent),
            Float("major-wound-stamina-penalty", Wounds, "94E49128B08ED6F2F2A82EB7AD956ED4395EF9D946E0B0E7C4680BBE7090435E", 2717, 2577, .30f, woundStaminaPenaltyPercent),
            Float("minor-poison-sleep-healing", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2389, 10f, poisonRecoveryPercent),
            Float("major-poison-sleep-healing", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2591, 10f, poisonRecoveryPercent),
            Float("minor-poison-liquid-healing", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2418, 15f, poisonRecoveryPercent),
            Float("major-poison-liquid-healing", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2620, 15f, poisonRecoveryPercent),
            Float("rest-delay", Vitality, "1CECDF167848D55AF1A12082E7655C5290589FE7F81DA84810A5CB9DAFB83F26", 2045, 1945, 1.5f, restDelayPercent),
            Float("exhaustion-threshold", Vitality, "1CECDF167848D55AF1A12082E7655C5290589FE7F81DA84810A5CB9DAFB83F26", 2045, 1916, .5f, exhaustionThresholdPercent),
            Float("exhaustion-penalty", Vitality, "1CECDF167848D55AF1A12082E7655C5290589FE7F81DA84810A5CB9DAFB83F26", 2045, 1974, .15f, exhaustionPenaltyPercent),
            Float("wound-recovery-duration", Wounds, "94E49128B08ED6F2F2A82EB7AD956ED4395EF9D946E0B0E7C4680BBE7090435E", 2717, 2519, 480f, woundRecoveryDurationPercent),
            Float("poison-stamina-penalty", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2649, .25f, poisonStaminaPenaltyPercent),
        };
        if (includeClan)
        {
            patches.Add(Float("clan-food", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 1795, 24f, foodPercent));
            patches.Add(Float("clan-water", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 1968, 30f, waterPercent));
            patches.Add(Float("clan-sleep", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 2170, 16f, sleepPercent));
        }
        return patches;
    }

    public static IReadOnlyList<GameplayAssetPatch> CreateVersion2(
        int foodPercent,
        int waterPercent,
        int sleepPercent,
        int fallPercent,
        int bleedingPercent,
        int poisonPercent) =>
    [
        Float("food", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 1795, 24f, foodPercent),
        Float("water", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 1968, 30f, waterPercent),
        Float("sleep", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 2170, 16f, sleepPercent),
        Float("minor-fall", Damage, "F980291295F36145ECC069D54F3FB4F171ECAD42F514F13EEFFD00F9002B6D26", 1723, 1621, .025f, fallPercent),
        Float("major-fall", Damage, "F980291295F36145ECC069D54F3FB4F171ECAD42F514F13EEFFD00F9002B6D26", 1723, 1699, .05f, fallPercent),
        Float("minor-bleed", Bleed, "F99BEBCD2DD078781B44BAAD90CC2A86EC44E3DD894A7320C64CAE0D213FF3F1", 2754, 2466, .01f, bleedingPercent),
        Float("major-bleed", Bleed, "F99BEBCD2DD078781B44BAAD90CC2A86EC44E3DD894A7320C64CAE0D213FF3F1", 2754, 2610, .02f, bleedingPercent),
        Float("minor-poison", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2447, .01f, poisonPercent),
        Float("major-poison", Poison, "A7A98CD23F0B93801F23E6375631FE13FEB93B5650E6668A620975D750BF6BE3", 2847, 2678, .02f, poisonPercent),
    ];

    public static IReadOnlyList<GameplayAssetPatch> CreateLegacy(int foodPercent, int waterPercent, int sleepPercent, int fallPercent) =>
    [
        Float("food", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 1795, 24f, foodPercent),
        Float("water", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 1968, 30f, waterPercent),
        Float("sleep", Regimen, "514897AB7A19C2E36E90C0CEB5DB26217A9F90493A9241DDEC6AE83096069D9D", 2513, 2170, 16f, sleepPercent),
        Float("minor-fall", Damage, "F980291295F36145ECC069D54F3FB4F171ECAD42F514F13EEFFD00F9002B6D26", 1723, 1621, .025f, fallPercent),
        Float("major-fall", Damage, "F980291295F36145ECC069D54F3FB4F171ECAD42F514F13EEFFD00F9002B6D26", 1723, 1699, .05f, fallPercent),
    ];

    private static GameplayAssetPatch Float(string id, string asset, string hash, int maxSize, int offset, float stock, int percent)
    {
        if (percent is < 10 or > 1000 || percent % 10 != 0) throw new ArgumentOutOfRangeException(nameof(percent));
        return new(id, Pak, asset, hash, maxSize, [new GameplayByteMutation(offset, Bytes(stock), Bytes(stock * percent / 100f))]);
    }

    private static byte[] Bytes(float value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
        return bytes;
    }
}
