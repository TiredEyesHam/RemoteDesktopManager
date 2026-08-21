namespace Patchbay.Core.Diagnostics;

/// <summary>
/// Which member names are treated as secrets, and what a redacted value looks
/// like (M3-08).
///
/// <para>
/// One list with three readers. <see cref="SecretRedactingPolicy"/> uses it on
/// the members of an object being destructured, <see cref="SecretRedactingEnricher"/>
/// uses it on the properties of a finished log event, and
/// <c>ArchitectureTests.Anything_holding_a_secret_overrides_ToString</c> uses
/// it to decide which types have to override <c>ToString</c>. Three copies of
/// the same list would drift, and the one that drifted would be the one
/// nobody was watching.
/// </para>
/// </summary>
public static class SecretNames
{
    /// <summary>
    /// What a redacted value is replaced with. Fixed width, so the length does
    /// not leak either.
    /// </summary>
    public const string Mask = "••••••••";

    /// <summary>
    /// Substrings that make a member name suspect, matched case-sensitively
    /// because .NET member names are PascalCase and a case-insensitive match
    /// on "token" would catch words that merely contain it.
    ///
    /// <para>
    /// Passphrase, Token and ApiKey have no member in Patchbay today. They are
    /// here so that the first one to arrive is redacted before anybody
    /// remembers to come back to this file.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Telltale { get; } =
        ["Password", "Secret", "Passphrase", "Token", "ApiKey"];

    /// <summary>
    /// Whether a member of this name might hold a secret. A name test only:
    /// whether the value actually needs hiding is <see cref="Redacts"/>.
    /// </summary>
    public static bool LooksLikeSecret(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (string telltale in Telltale)
        {
            if (name.Contains(telltale, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether this member of this value has to be hidden.
    ///
    /// <para>
    /// A name alone is not enough. <c>HasPassword</c>, <c>IsSecret</c> and a
    /// count of imported passwords all read as secrets by name and none of
    /// them is one: they are facts <em>about</em> a secret, and they are
    /// usually the fact somebody reading a log actually wants. So anything
    /// that arrives as a bool, a number, an enum or a date is left alone, and
    /// what gets masked is the strings and the buffers.
    /// </para>
    /// </summary>
    public static bool Redacts(string name, object? value) =>
        LooksLikeSecret(name) && !IsFactAboutOne(value);

    /// <summary>
    /// Whether a value is too small to be a secret and too useful to lose.
    /// Null counts: a null password is worth seeing, and masking it would say
    /// there was one.
    /// </summary>
    private static bool IsFactAboutOne(object? value) => value switch
    {
        null => true,
        Enum => true,
        DateTime or DateTimeOffset or TimeSpan or Guid => true,
        _ => value.GetType().IsPrimitive || value is decimal,
    };
}
