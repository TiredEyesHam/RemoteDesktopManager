namespace Patchbay.Core.Security;

/// <summary>
/// The clipboard that is not there (M3-09). What <c>Core</c> uses when nobody
/// has given it a real one.
///
/// <para>
/// Refuses rather than pretends, in the same way as
/// <see cref="UnavailableSecretProtector"/>. A copy that silently did nothing
/// would leave somebody pasting the previous contents of their clipboard into
/// a logon box and wondering why the password was wrong.
/// </para>
/// </summary>
public sealed class UnavailableClipboard : ISystemClipboard
{
    /// <summary>The one instance. It has no state and never will.</summary>
    public static UnavailableClipboard Instance { get; } = new();

    private UnavailableClipboard()
    {
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public long Token => 0;

    /// <inheritdoc />
    public bool SetSecret(Secret secret) => false;

    /// <inheritdoc />
    public bool SetText(string text) => false;

    /// <inheritdoc />
    public bool Clear() => false;
}
