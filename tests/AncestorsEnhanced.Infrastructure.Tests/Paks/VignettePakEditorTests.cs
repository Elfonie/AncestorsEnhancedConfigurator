using AncestorsEnhanced.Infrastructure.Paks;

namespace AncestorsEnhanced.Infrastructure.Tests.Paks;

public sealed class VignettePakEditorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(50)]
    [InlineData(100)]
    public void ScaleCanBeReadBack(int percent)
    {
        byte[] original = CreateAsset();
        byte[] scaled = VignettePakEditor.Scale(original, percent);

        Assert.True(VignettePakEditor.TryReadPercent(original, scaled, out decimal detected));
        Assert.Equal(percent, detected);
    }

    [Fact]
    public void OtherAssetChangesAreRejected()
    {
        byte[] original = CreateAsset();
        byte[] changed = VignettePakEditor.Scale(original, 50);
        changed[10] ^= 1;

        Assert.False(VignettePakEditor.TryReadPercent(original, changed, out _));
    }

    private static byte[] CreateAsset()
    {
        byte[] asset = new byte[0x500];
        foreach (int offset in new[] { 0x41A, 0x435, 0x439, 0x441, 0x450 })
        {
            BitConverter.GetBytes(0.8f).CopyTo(asset, offset);
        }

        return asset;
    }
}
