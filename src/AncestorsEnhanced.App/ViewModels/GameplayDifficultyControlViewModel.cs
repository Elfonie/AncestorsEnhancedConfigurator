using CommunityToolkit.Mvvm.ComponentModel;

namespace AncestorsEnhanced.App.ViewModels;

public sealed partial class GameplayDifficultyControlViewModel : ViewModelBase
{
    private const int DefaultMinPercent = 10;
    private const int DefaultMaxPercent = 200;

    [ObservableProperty]
    public partial int MultiplierPercent { get; set; } = 100;

    [ObservableProperty]
    public partial int MinPercent { get; set; } = DefaultMinPercent;

    [ObservableProperty]
    public partial int MaxPercent { get; set; } = DefaultMaxPercent;

    public GameplayDifficultyControlViewModel(string id, string name, string stockValue, string description, bool higherIsHarder)
    {
        Id = id;
        Name = name;
        StockValue = stockValue;
        Description = description;
        HigherIsHarder = higherIsHarder;
    }

    public string Id { get; }

    public string Name { get; }

    public string StockValue { get; }

    public string Description { get; }

    public bool HigherIsHarder { get; }

    public string DraftValue => $"{MultiplierPercent}% of game default";

    partial void OnMultiplierPercentChanged(int value)
    {
        int clamped = Math.Clamp(value, MinPercent, MaxPercent);
        if (clamped != value)
        {
            MultiplierPercent = clamped;
            return;
        }
        OnPropertyChanged(nameof(DraftValue));
    }

    partial void OnMaxPercentChanged(int value)
    {
        if (MultiplierPercent > value)
        {
            MultiplierPercent = value;
        }
    }

    partial void OnMinPercentChanged(int value)
    {
        if (MultiplierPercent < value)
        {
            MultiplierPercent = value;
        }
    }
}
