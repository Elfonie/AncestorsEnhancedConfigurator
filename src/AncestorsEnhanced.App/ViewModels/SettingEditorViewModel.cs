using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SettingEditorViewModel : ViewModelBase
{
    private readonly SettingEditSnapshot _snapshot;
    private readonly string? _initialValue;

    [ObservableProperty]
    private bool _useCustomValue;

    [ObservableProperty]
    private bool _toggleValue;

    [ObservableProperty]
    private decimal _numberValue;

    [ObservableProperty]
    private SettingChoiceViewModel? _selectedChoice;

    public SettingEditorViewModel(SettingEditSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _initialValue = Normalize(snapshot.CurrentOverride);
        UseCustomValue = snapshot.CurrentOverride is not null;

        string startingValue = snapshot.CurrentOverride ?? snapshot.DefaultValue;
        ToggleValue = startingValue == "1";
        NumberValue = decimal.TryParse(
            startingValue,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out decimal number)
            ? number
            : 0;

        Choices = snapshot.Choices?
            .Select(choice => new SettingChoiceViewModel(choice.Value, choice.Label))
            .ToArray() ?? [];
        SelectedChoice = Choices.FirstOrDefault(choice =>
                             string.Equals(choice.Value, startingValue, StringComparison.Ordinal))
                         ?? Choices.FirstOrDefault(choice =>
                             string.Equals(choice.Value, snapshot.DefaultValue, StringComparison.Ordinal));
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SettingChoiceViewModel> Choices { get; }

    public bool IsToggle => _snapshot.Kind == SettingEditorKind.Toggle;

    public bool IsNumber => _snapshot.Kind == SettingEditorKind.Number;

    public bool IsChoice => _snapshot.Kind == SettingEditorKind.Choice;

    public bool IsPresence => _snapshot.Kind == SettingEditorKind.Presence;

    public bool IsRegularEditor => !IsPresence;

    public decimal Minimum => _snapshot.Minimum ?? 0;

    public decimal Maximum => _snapshot.Maximum ?? 100;

    public decimal Increment => _snapshot.Increment ?? 1;

    public string ModeLabel => IsPresence
        ? UseCustomValue ? "Videos skipped" : "Game default"
        : UseCustomValue ? "Custom value" : "Game controlled";

    public bool HasChanges => !string.Equals(
        UseCustomValue ? DesiredValue : null,
        _initialValue,
        StringComparison.Ordinal);

    public string DesiredSummary => IsPresence
        ? UseCustomValue ? "Skip videos" : "Use game default"
        : UseCustomValue
        ? IsChoice
            ? SelectedChoice?.Label ?? "Choose a value"
            : IsToggle
                ? ToggleValue ? "On" : "Off"
                : DesiredValue ?? "Invalid value"
        : "Use game preset";

    public SettingChangeRequest CreateRequest(string settingId, string displayName) =>
        new(
            settingId,
            displayName,
            _snapshot.FileName,
            _snapshot.Section,
            _snapshot.Key,
            UseCustomValue ? DesiredValue : null);

    public void Reset()
    {
        string startingValue = _snapshot.CurrentOverride ?? _snapshot.DefaultValue;
        UseCustomValue = _snapshot.CurrentOverride is not null;
        ToggleValue = startingValue == "1";
        if (decimal.TryParse(
                startingValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal number))
        {
            NumberValue = number;
        }

        SelectedChoice = Choices.FirstOrDefault(choice =>
            string.Equals(choice.Value, startingValue, StringComparison.Ordinal));
        NotifyStateChanged();
    }

    partial void OnUseCustomValueChanged(bool value) => NotifyStateChanged();

    partial void OnToggleValueChanged(bool value) => NotifyStateChanged();

    partial void OnNumberValueChanged(decimal value) => NotifyStateChanged();

    partial void OnSelectedChoiceChanged(SettingChoiceViewModel? value) => NotifyStateChanged();

    private string? DesiredValue => _snapshot.Kind switch
    {
        SettingEditorKind.Toggle => ToggleValue ? "1" : "0",
        SettingEditorKind.Number => NumberValue.ToString(CultureInfo.InvariantCulture),
        SettingEditorKind.Choice => SelectedChoice?.Value,
        SettingEditorKind.Presence => _snapshot.DefaultValue,
        _ => null,
    };

    private string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (_snapshot.Kind == SettingEditorKind.Number && decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return value;
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(DesiredSummary));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
