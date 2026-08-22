using Patchbay.App.Security;
using Patchbay.Core.Security;

namespace Patchbay.App.ViewModels;

/// <summary>
/// One place a document can keep its saved passwords, as a row in the security
/// panel (M3-04).
///
/// <para>
/// The words are here rather than in <c>Core</c> because they are the whole of
/// what the choice is. The two Windows stores are the same cryptography under
/// a different roof: neither is stronger, and picking between them on strength
/// would be picking at random. What differs is what a copy of the document
/// carries with it, and somebody choosing has to be told that in a sentence
/// rather than left to infer it from a scheme name.
/// </para>
/// </summary>
/// <param name="Scheme">The <see cref="SecretEnvelope"/> scheme name, which is what gets chosen.</param>
/// <param name="Label">What the button says.</param>
/// <param name="Description">What choosing it means, in one sentence about the file.</param>
/// <param name="IsAvailable">Whether this machine can actually use it.</param>
/// <param name="IsCurrent">Whether it is the one in use.</param>
public sealed record SecretStoreOption(
    string Scheme,
    string Label,
    string Description,
    bool IsAvailable,
    bool IsCurrent)
{
    /// <summary>Whether picking this one would do anything.</summary>
    public bool CanChoose => IsAvailable && !IsCurrent;

    /// <summary>The name to show for a scheme.</summary>
    public static string LabelFor(string scheme) => scheme switch
    {
        DpapiSecretProtector.SchemeName => "Windows data protection",
        CredentialManagerSecretProtector.SchemeName => "Windows Credential Manager",
        _ => scheme,
    };

    /// <summary>What choosing it means.</summary>
    public static string DescriptionFor(string scheme) => scheme switch
    {
        DpapiSecretProtector.SchemeName =>
            "Kept inside this document, encrypted for this Windows account. A copy of the file "
            + "carries the passwords with it, unreadable to anyone else.",
        CredentialManagerSecretProtector.SchemeName =>
            "Kept in Windows, outside this document. A copy of the file carries no password "
            + "material at all — and neither do the backups. Restore it on another machine and "
            + "the passwords are not there.",
        _ =>
            "This version of Patchbay does not have this store, so passwords cannot be saved "
            + "until another one is chosen.",
    };
}
