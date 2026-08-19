namespace Patchbay.Core.Inheritance;

/// <summary>What kind of control a setting needs.</summary>
public enum SettingKind
{
    Text,
    Number,
    Boolean,
    Choice,

    /// <summary>
    /// Real, inherited, and deliberately not editable by hand. Credential
    /// profile ids are picked from a list the credential store owns (M3-04),
    /// so a text box full of GUIDs would be worse than no control at all.
    /// </summary>
    Hidden,
}

/// <summary>
/// Everything the interface needs to know about one setting: what to call it,
/// where it belongs, and what sort of control it takes.
/// </summary>
/// <param name="PropertyName">Name on <see cref="Model.ConnectionSettings"/>.</param>
/// <param name="Label">Wording shown next to the control.</param>
/// <param name="Section">Heading it sits under.</param>
/// <param name="Kind">Control to use.</param>
/// <param name="ValueType">Underlying type, with nullability stripped.</param>
/// <param name="Hint">Optional sentence under the control.</param>
public sealed record SettingDescriptor(
    string PropertyName,
    string Label,
    string Section,
    SettingKind Kind,
    Type ValueType,
    string? Hint = null);
