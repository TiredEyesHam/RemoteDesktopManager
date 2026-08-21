namespace Patchbay.Core.Security;

/// <summary>
/// The clipboard, as much of it as Patchbay needs (M3-09).
///
/// <para>
/// An interface because the real one is platform code and because the rules
/// worth getting right are not. When to clear, and whether it is still ours to
/// clear, live in <see cref="SecretClipboard"/> in <c>Core</c> where there are
/// tests; what is left here is four calls to Windows.
/// </para>
/// </summary>
public interface ISystemClipboard
{
    /// <summary>
    /// Whether there is a clipboard to write to. False in a test, and on
    /// anything that is not a desktop.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A number that changes whenever anything is put on the clipboard, by
    /// anybody.
    ///
    /// <para>
    /// This is how "is what I put there still there?" gets answered without
    /// reading the clipboard back and comparing. Reading it back would mean
    /// holding the password to compare against, which is the thing being
    /// avoided; and it would be wrong anyway, because two copies of the same
    /// text are indistinguishable and only one of them is ours.
    /// </para>
    /// </summary>
    long Token { get; }

    /// <summary>
    /// Puts a password on the clipboard, marked so that Windows keeps it out
    /// of clipboard history and off the cloud clipboard.
    ///
    /// <para>
    /// Takes the <see cref="Secret"/> rather than a string so that revealing
    /// it happens here, at the edge, and not in whatever called this.
    /// </para>
    /// </summary>
    /// <returns>False when the clipboard would not take it.</returns>
    bool SetSecret(Secret secret);

    /// <summary>
    /// Puts ordinary text on the clipboard, with none of the exclusions.
    ///
    /// <para>
    /// A user name is not a secret. Keeping it out of clipboard history would
    /// cost somebody a feature they use and buy nothing, since the same name
    /// is sitting in the connection document in the clear.
    /// </para>
    /// </summary>
    bool SetText(string text);

    /// <summary>Empties the clipboard. False when it would not be emptied.</summary>
    bool Clear();
}
