namespace Patchbay.Core.Inheritance;

/// <summary>What kind of control a setting needs.</summary>
public enum SettingKind
{
    Text,
    Number,
    Boolean,
    Choice,

    /// <summary>
    /// A saved sign-in, chosen from the ones the document holds (M3-10). Its
    /// own kind rather than a <see cref="Choice"/> because the options are not
    /// a fixed set the catalogue can name — they change as somebody adds and
    /// deletes profiles, so whoever builds the field has to supply them.
    /// </summary>
    Credential,

    /// <summary>
    /// Real, inherited, and deliberately not editable by hand. Nothing uses
    /// this now that credential profiles are picked from a list
    /// (<see cref="Credential"/>), and it is kept because the catalogue's
    /// contract is that a kind means one control and the next unpickable
    /// setting should not have to reinvent it.
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
