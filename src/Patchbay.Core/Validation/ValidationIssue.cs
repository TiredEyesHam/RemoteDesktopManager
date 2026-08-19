namespace Patchbay.Core.Validation;

/// <summary>
/// One thing wrong with what someone has typed, tied to the field that has to
/// light up. <see cref="Field"/> is a property name from the editor, not a
/// label, so the binding can find the box without matching on English.
/// </summary>
/// <param name="Field">Name of the field at fault.</param>
/// <param name="Message">Sentence to show under it.</param>
public sealed record ValidationIssue(string Field, string Message);
