using AncestorsEnhanced.Core.Editing;
using AncestorsEnhanced.Infrastructure.Editing;

namespace AncestorsEnhanced.Infrastructure.Tests.Editing;

public sealed class IniDocumentEditorTests
{
    [Fact]
    public void ApplyPreservesUnrelatedContentAndLineEndings()
    {
        const string Original =
            "; keep this\r\n" +
            "[SystemSettings]\r\n" +
            "r.ViewDistanceScale=1.0\r\n" +
            "r.MotionBlurQuality=4\r\n" +
            "r.ViewDistanceScale=1.1\r\n" +
            "\r\n" +
            "[Other]\r\n" +
            "Keep=Yes\r\n";

        SettingChangeRequest[] changes =
        [
            Change("r.ViewDistanceScale", "1.2"),
            Change("r.MotionBlurQuality", null),
            Change("r.Tonemapper.Sharpen", "0.4"),
        ];

        string result = IniDocumentEditor.Apply(Original, changes);

        Assert.Equal(
            "; keep this\r\n" +
            "[SystemSettings]\r\n" +
            "r.ViewDistanceScale=1.0\r\n" +
            "r.ViewDistanceScale=1.2\r\n" +
            "\r\n" +
            "r.Tonemapper.Sharpen=0.4\r\n" +
            "[Other]\r\n" +
            "Keep=Yes\r\n",
            result);
    }

    [Fact]
    public void ApplyCreatesMissingSectionWithoutReplacingTheDocument()
    {
        const string Original = "[Other]\nKeep=Yes\n";

        string result = IniDocumentEditor.Apply(
            Original,
            [Change("r.MaxAnisotropy", "16")]);

        Assert.Equal(
            "[Other]\nKeep=Yes\n\n[SystemSettings]\nr.MaxAnisotropy=16\n",
            result);
    }

    private static SettingChangeRequest Change(string key, string? value) =>
        new(key, key, "Engine.ini", "SystemSettings", key, value);
}
