using AncestorsEnhanced.Core.SaveGames;

namespace AncestorsEnhanced.Core.Tests;

public sealed class ByteRangeTests
{
    [Fact]
    public void EndExclusiveUses64BitArithmeticToAvoidOverflow()
    {
        ByteRange range = new(int.MaxValue, int.MaxValue);

        Assert.Equal((long)int.MaxValue + int.MaxValue, range.EndExclusive);
    }

    [Fact]
    public void OverflowingRangeIsLargerThanAnyRealPayload()
    {
        ByteRange range = new(int.MaxValue, int.MaxValue);
        int payloadLength = 1024;

        Assert.True(range.EndExclusive > payloadLength);
    }
}
