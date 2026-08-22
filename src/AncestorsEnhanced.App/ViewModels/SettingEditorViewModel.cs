using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SettingEditorViewModel : ViewModelBase
{
    private readonly SettingEditSnapshot _snapshot;
    private readonly string? _initialValue;

    [ObservableProperty]
    public partial bool UseCustomValue { get; set; }

    [ObservableProperty]
    public partial bool ToggleValue { get; set; }

    [ObservableProperty]
    public partial decimal NumberValue { get; set; }

    [ObservableProperty]
    public partial SettingChoiceViewModel? SelectedChoice { get; set; }

    public SettingEditorViewModel(SettingEditSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _initialValue = Normalize(snapshot.CurrentOverride);
        UseCustomValue = snapshot.CurrentOverride is not null;

        string startingValue = StartingValue();
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

    public string Unit => _snapshot.Unit ?? string.Empty;

    public bool HasUnit => _snapshot.Unit is not null;

    public bool ShowOverrideToggle => !_snapshot.IsDirect;

    public bool CanSetCustomValue => _snapshot.CanSetCustomValue;

    public bool IsCustomEditorEnabled => UseCustomValue && CanSetCustomValue;

    public bool HasUnsupportedCurrentValue => UseCustomValue && !CanSetCustomValue;

    public bool HasActiveOverride => ShowOverrideToggle && _initialValue is not null;

    public bool HasCurrentOverride => _snapshot.CurrentOverride is not null;

    public bool HasKnownGameValue => _snapshot.GameControlledValue is not null;

    public bool ShowValueEditor =>
        CanSetCustomValue &&
        (_snapshot.IsDirect || UseCustomValue || HasKnownGameValue);

    public bool ShowUnknownGameValue =>
        IsRegularEditor &&
        ShowOverrideToggle &&
        !UseCustomValue &&
        !HasKnownGameValue &&
        CanSetCustomValue;

    public bool HasChanges => !HasUnsupportedCurrentValue && !string.Equals(
            _snapshot.IsDirect || UseCustomValue ? DesiredValue : null,
        _initialValue,
        StringComparison.Ordinal);

    public string DesiredSummary => HasUnsupportedCurrentValue
        ? "Unsupported override; reset available"
        : IsPresence
        ? UseCustomValue ? "Skip videos" : "Use game default"
        : UseCustomValue
        ? IsChoice
            ? SelectedChoice?.Label ?? "Choose a value"
            : IsToggle
                ? ToggleValue ? "On" : "Off"
                : $"{DesiredValue ?? "Invalid value"}{Unit}"
        : "Use game default";

    public SettingChangeRequest CreateRequest(string displayName) =>
        new(
            displayName,
            _snapshot.FileName,
            _snapshot.Section,
            _snapshot.Key,
            _snapshot.IsDirect || UseCustomValue ? DesiredValue : null);

    public void Reset()
    {
        string startingValue = StartingValue();
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
            string.Equals(choice.Value, startingValue, StringComparison.Ordinal))
            ?? Choices.FirstOrDefault(choice =>
                string.Equals(choice.Value, _snapshot.DefaultValue, StringComparison.Ordinal));
        NotifyStateChanged();
    }

    public void UseGameDefault()
    {
        if (ShowOverrideToggle)
        {
            UseCustomValue = false;
        }
    }

    partial void OnUseCustomValueChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCustomEditorEnabled));
        OnPropertyChanged(nameof(HasUnsupportedCurrentValue));
        OnPropertyChanged(nameof(ShowValueEditor));
        OnPropertyChanged(nameof(ShowUnknownGameValue));
        NotifyStateChanged();
    }

    partial void OnToggleValueChanged(bool value)
    {
        NotifyStateChanged();
    }

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

    private string StartingValue() =>
        _snapshot.CurrentOverride ??
        _snapshot.GameControlledValue ??
        _snapshot.DefaultValue;

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(DesiredSummary));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
