namespace AncestorsEnhanced.App.ViewModels;

public sealed record ReadableSettingRowViewModel(
    string Category,
    string Name,
    string Value,
    string Description,
    string Source,
    string AccentColor,
    double ProgressValue,
    bool HasProgress);
