using System.Globalization;
using AncestorsEnhanced.Core.Editing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AncestorsEnhanced.App.ViewModels;

public partial class SettingEditorViewModel : ViewModelBase
{
    private readonly SettingEditSnapshot _snapshot;
    private readonly string? _initialValue;
    private bool _suppressAutoCustom = true;

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
            ? number * _snapshot.DisplayMultiplier
            : 0;

        Choices = snapshot.Choices?
            .Select(choice => new SettingChoiceViewModel(choice.Value, choice.Label))
            .ToArray() ?? [];
        SelectedChoice = Choices.FirstOrDefault(choice =>
                             string.Equals(choice.Value, startingValue, StringComparison.Ordinal))
                         ?? Choices.FirstOrDefault(choice =>
                             string.Equals(choice.Value, snapshot.DefaultValue, StringComparison.Ordinal));
        _suppressAutoCustom = false;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SettingChoiceViewModel> Choices { get; }

    public string Key => _snapshot.Key;

    public bool IsToggle => _snapshot.Kind == SettingEditorKind.Toggle;

    public bool IsNumber => _snapshot.Kind == SettingEditorKind.Number;

    public bool IsChoice => _snapshot.Kind == SettingEditorKind.Choice;

    public bool IsPresence => _snapshot.Kind == SettingEditorKind.Presence;

    public bool IsRegularEditor => !IsPresence;

    public decimal Minimum => (_snapshot.Minimum ?? 0) * _snapshot.DisplayMultiplier;

    public decimal Maximum => (_snapshot.Maximum ?? 100) * _snapshot.DisplayMultiplier;

    public decimal Increment => (_snapshot.Increment ?? 1) * _snapshot.DisplayMultiplier;

    public string Unit => _snapshot.Unit ?? string.Empty;

    public bool HasUnit => _snapshot.Unit is not null;

    public bool ShowOverrideToggle => !_snapshot.IsDirect;

    public bool CanSetCustomValue => _snapshot.CanSetCustomValue;

    public bool IsCustomEditorEnabled => CanSetCustomValue;

    public bool HasUnsupportedCurrentValue => UseCustomValue && !CanSetCustomValue;

    public bool HasActiveOverride => ShowOverrideToggle && _initialValue is not null;

    public bool HasCurrentOverride => _snapshot.CurrentOverride is not null;

    public bool HasKnownGameValue => _snapshot.GameControlledValue is not null;

    public bool ShowValueEditor => CanSetCustomValue;

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
        ? FormatValue(DesiredValue)
        : "Use game default";

    /// <summary>Formats a stored config value in the same terms shown by the editor.</summary>
    public string FormatValue(string? rawValue)
    {
        if (rawValue is null)
        {
            return "Game default";
        }

        if (IsToggle)
        {
            return rawValue == "1" ? "On" : rawValue == "0" ? "Off" : rawValue;
        }

        if (IsChoice)
        {
            return Choices.FirstOrDefault(choice => string.Equals(choice.Value, rawValue, StringComparison.Ordinal))?.Label
                ?? rawValue;
        }

        if (IsNumber && decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
        {
            decimal displayValue = number * _snapshot.DisplayMultiplier;
            return $"{displayValue.ToString(CultureInfo.CurrentCulture)}{Unit}";
        }

        return rawValue;
    }

    public SettingChangeRequest CreateRequest(string displayName) =>
        new(
            displayName,
            _snapshot.FileName,
            _snapshot.Section,
            _snapshot.Key,
            _snapshot.IsDirect || UseCustomValue ? DesiredValue : null);

    public bool TryGetCustomProfileValue(out string? value)
    {
        value = null;
        if ((!ShowOverrideToggle && !_snapshot.IsDirect) ||
            (!UseCustomValue && !_snapshot.IsDirect) ||
            HasUnsupportedCurrentValue)
        {
            return false;
        }

        value = DesiredValue;
        return value is not null;
    }

    /// <summary>
    /// Returns the unformatted value that a profile would compare against. UI labels
    /// deliberately use localized/display formatting, which must not be compared to
    /// the serialized profile representation.
    /// </summary>
    public string? GetProfileComparisonValue() =>
        _snapshot.IsDirect || UseCustomValue
            ? DesiredValue
            : _snapshot.GameControlledValue ?? _snapshot.DefaultValue;

    public bool TryApplyProfileValue(string? value)
    {
        if (!CanApplyProfileValue(value))
        {
            return false;
        }

        switch (_snapshot.Kind)
        {
            case SettingEditorKind.Toggle:
                UseCustomValue = true;
                ToggleValue = value == "1";
                return true;
            case SettingEditorKind.Number:
                decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number);
                UseCustomValue = true;
                NumberValue = number * _snapshot.DisplayMultiplier;
                return true;
            case SettingEditorKind.Choice:
                SettingChoiceViewModel? choice = Choices.FirstOrDefault(candidate =>
                    string.Equals(candidate.Value, value, StringComparison.Ordinal));
                if (choice is null)
                {
                    return false;
                }
                UseCustomValue = true;
                SelectedChoice = choice;
                return true;
            case SettingEditorKind.Presence:
                UseCustomValue = true;
                return true;
            default:
                return false;
        }
    }

    public bool CanApplyProfileValue(string? value)
    {
        if (!CanSetCustomValue || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return _snapshot.Kind switch
        {
            SettingEditorKind.Toggle => value is "0" or "1",
            SettingEditorKind.Number when decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal number) =>
                number >= (_snapshot.Minimum ?? decimal.MinValue) &&
                number <= (_snapshot.Maximum ?? decimal.MaxValue) &&
                IsOnIncrement(number),
            SettingEditorKind.Choice => Choices.Any(candidate =>
                string.Equals(candidate.Value, value, StringComparison.Ordinal)),
            SettingEditorKind.Presence => string.Equals(
                value,
                _snapshot.DefaultValue,
                StringComparison.Ordinal),
            _ => false,
        };
    }

    public void Reset()
    {
        _suppressAutoCustom = true;
        string startingValue = StartingValue();
        UseCustomValue = _snapshot.CurrentOverride is not null;
        ToggleValue = startingValue == "1";
        if (decimal.TryParse(
                startingValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal number))
        {
            NumberValue = number * _snapshot.DisplayMultiplier;
        }

        SelectedChoice = Choices.FirstOrDefault(choice =>
            string.Equals(choice.Value, startingValue, StringComparison.Ordinal))
            ?? Choices.FirstOrDefault(choice =>
                string.Equals(choice.Value, _snapshot.DefaultValue, StringComparison.Ordinal));
        _suppressAutoCustom = false;
        NotifyStateChanged();
    }

    public void UseGameDefault()
    {
        if (ShowOverrideToggle)
        {
            _suppressAutoCustom = true;
            UseCustomValue = false;
            string startingValue = _snapshot.GameControlledValue ?? _snapshot.DefaultValue;
            ToggleValue = startingValue == "1";
            if (decimal.TryParse(
                    startingValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out decimal number))
            {
                NumberValue = number * _snapshot.DisplayMultiplier;
            }

            SelectedChoice = Choices.FirstOrDefault(choice =>
                string.Equals(choice.Value, startingValue, StringComparison.Ordinal))
                ?? Choices.FirstOrDefault(choice =>
                    string.Equals(choice.Value, _snapshot.DefaultValue, StringComparison.Ordinal));
            _suppressAutoCustom = false;
            NotifyStateChanged();
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
        if (!_suppressAutoCustom)
        {
            UseCustomValue = true;
        }
        NotifyStateChanged();
    }

    partial void OnNumberValueChanged(decimal value)
    {
        if (!_suppressAutoCustom)
        {
            UseCustomValue = true;
        }
        NotifyStateChanged();
    }

    partial void OnSelectedChoiceChanged(SettingChoiceViewModel? value)
    {
        if (!_suppressAutoCustom && value is not null)
        {
            UseCustomValue = true;
        }
        NotifyStateChanged();
    }

    private string? DesiredValue => _snapshot.Kind switch
    {
        SettingEditorKind.Toggle => ToggleValue ? "1" : "0",
        SettingEditorKind.Number => (NumberValue / _snapshot.DisplayMultiplier).ToString(CultureInfo.InvariantCulture),
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

    private bool IsOnIncrement(decimal value)
    {
        if (_snapshot.Increment is not > 0 || _snapshot.Minimum is null)
        {
            return true;
        }

        decimal steps = (value - _snapshot.Minimum.Value) / _snapshot.Increment.Value;
        return decimal.Abs(steps - decimal.Round(steps)) < 0.000001m;
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(DesiredSummary));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
