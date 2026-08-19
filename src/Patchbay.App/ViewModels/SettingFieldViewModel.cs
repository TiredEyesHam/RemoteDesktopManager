using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;

namespace Patchbay.App.ViewModels;

/// <summary>One selectable value of a choice setting, with its wording.</summary>
public sealed record ChoiceOption(object Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One editable setting, with the override toggle that is the whole point of
/// the inheritance model.
///
/// Off, the field shows what the node would inherit and where from, and holds
/// null in the draft. On, it holds a value of its own. Starting an override
/// copies the inherited value in first, so turning the switch on and changing
/// nothing is genuinely a no-op rather than a silent reset to zero.
/// </summary>
public sealed partial class SettingFieldViewModel : ObservableObject
{
    private readonly ConnectionSettings _draft;
    private readonly object? _inheritedValue;
    private bool _loading = true;

    [ObservableProperty]
    private bool _isOverridden;

    [ObservableProperty]
    private string _textValue = string.Empty;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private ChoiceOption? _choiceValue;

    [ObservableProperty]
    private string? _error;

    /// <param name="descriptor">Which setting this is.</param>
    /// <param name="draft">
    /// The settings object being edited. Writes land here immediately; nothing
    /// reaches the document until the editor is saved.
    /// </param>
    /// <param name="inherited">
    /// What the node would get from its ancestry if it overrode nothing, or
    /// null for the root, which inherits from the built-in defaults only.
    /// </param>
    public SettingFieldViewModel(
        SettingDescriptor descriptor,
        ConnectionSettings draft,
        EffectiveSettings? inherited)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(draft);

        Descriptor = descriptor;
        _draft = draft;

        _inheritedValue = inherited is null
            ? SettingCatalogue.Read(ConnectionSettings.Defaults, descriptor.PropertyName)
            : SettingCatalogue.Read(inherited.Values, descriptor.PropertyName);

        InheritedFrom = inherited?.SourceOf(descriptor.PropertyName)?.Name ?? "Default";

        if (descriptor.Kind is SettingKind.Choice)
        {
            Choices = [.. SettingDisplay.ChoicesFor(descriptor)
                .Select(value => new ChoiceOption(value, SettingDisplay.Describe(value)))];
        }

        object? own = SettingCatalogue.Read(draft, descriptor.PropertyName);
        _isOverridden = own is not null;

        Load(own ?? _inheritedValue);
        _loading = false;
    }

    public SettingDescriptor Descriptor { get; }

    public string Label => Descriptor.Label;

    public string? Hint => Descriptor.Hint;

    public IReadOnlyList<ChoiceOption> Choices { get; } = [];

    public bool IsText => Descriptor.Kind is SettingKind.Text;

    public bool IsNumber => Descriptor.Kind is SettingKind.Number;

    public bool IsBoolean => Descriptor.Kind is SettingKind.Boolean;

    public bool IsChoice => Descriptor.Kind is SettingKind.Choice;

    /// <summary>What the field would show if the override were switched off.</summary>
    public string InheritedText => SettingDisplay.Describe(_inheritedValue, Descriptor);

    /// <summary>The group the inherited value comes from, or "Default".</summary>
    public string InheritedFrom { get; }

    partial void OnIsOverriddenChanged(bool value)
    {
        if (!_loading && value)
        {
            // Switching an override on starts from what was being inherited,
            // so the act of overriding never changes the value by itself.
            Load(SettingCatalogue.Read(_draft, Descriptor.PropertyName) ?? _inheritedValue);
        }

        Push();
    }

    partial void OnTextValueChanged(string value) => Push();

    partial void OnBoolValueChanged(bool value) => Push();

    partial void OnChoiceValueChanged(ChoiceOption? value) => Push();

    /// <summary>Fills the controls from a value without writing anything back.</summary>
    private void Load(object? value)
    {
        bool wasLoading = _loading;
        _loading = true;

        try
        {
            switch (Descriptor.Kind)
            {
                case SettingKind.Boolean:
                    BoolValue = value is true;
                    break;

                case SettingKind.Choice:
                    ChoiceValue = Choices.FirstOrDefault(c => Equals(c.Value, value))
                        ?? (Choices.Count > 0 ? Choices[0] : null);
                    break;

                default:
                    TextValue = System.Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
                    break;
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    /// <summary>Writes the current control state into the draft.</summary>
    private void Push()
    {
        if (_loading)
        {
            return;
        }

        if (!IsOverridden)
        {
            // Null is what makes it inherit again.
            SettingCatalogue.Write(_draft, Descriptor.PropertyName, null);
            Error = null;
            return;
        }

        switch (Descriptor.Kind)
        {
            case SettingKind.Boolean:
                Write(BoolValue);
                break;

            case SettingKind.Choice:
                Write(ChoiceValue?.Value);
                break;

            case SettingKind.Number:
                if (string.IsNullOrWhiteSpace(TextValue))
                {
                    Error = "Enter a number, or switch the override off to inherit one.";
                }
                else if (int.TryParse(TextValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
                {
                    Write(number);
                }
                else
                {
                    Error = "This has to be a whole number.";
                }

                break;

            default:
                // An override set to nothing is a real choice for a user name
                // or a domain: it means "explicitly blank here", which is not
                // the same as inheriting one.
                Write(TextValue.Trim());
                break;
        }
    }

    private void Write(object? value)
    {
        SettingCatalogue.Write(_draft, Descriptor.PropertyName, value);
        Error = null;
    }
}
