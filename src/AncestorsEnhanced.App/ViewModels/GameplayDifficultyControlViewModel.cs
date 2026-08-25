using CommunityToolkit.Mvvm.ComponentModel;

namespace AncestorsEnhanced.App.ViewModels;

public sealed partial class GameplayDifficultyControlViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial int MultiplierPercent { get; set; } = 100;

    public GameplayDifficultyControlViewModel(string name, string stockValue, string description)
    {
        Name = name;
        StockValue = stockValue;
        Description = description;
    }

    public string Name { get; }

    public string StockValue { get; }

    public string Description { get; }

    public string DraftValue => $"{MultiplierPercent}% of game default";

    partial void OnMultiplierPercentChanged(int value) =>
        OnPropertyChanged(nameof(DraftValue));
}
