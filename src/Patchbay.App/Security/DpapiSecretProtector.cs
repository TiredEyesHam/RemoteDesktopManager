using System.Security.Cryptography;
using System.Text;
using Patchbay.Core.Security;

namespace Patchbay.App.Security;

/// <summary>
/// Protects secrets with Windows data protection, scoped to the signed-in
/// account (M3-02).
///
/// <para>
/// <b>Why <c>CurrentUser</c> and not <c>LocalMachine</c>.</b> A machine-scoped
/// blob can be unprotected by <em>any</em> account on the machine, including a
/// service account and including whoever comes to sit at it next. That is not
/// a smaller amount of protection than user scope, it is a different thing
/// altogether: it protects the file against being carried away and against
/// nothing else. User scope costs one real thing, and it is worth stating
/// plainly rather than discovering — a saved password does not travel. Copy
/// the connection file to another machine, or sign in as another user on this
/// one, and every saved password in it becomes unreadable. That is the
/// bargain, it is deliberate, and <see cref="SecretUnprotectStatus.Unreadable"/>
/// exists to explain it to whoever runs into it.
/// </para>
///
/// <para>
/// <b>The scope is chosen when protecting, and only then.</b> The scope
/// argument to <c>Unprotect</c> looks like a check and is not one: a
/// user-scoped blob opens perfectly well when <c>LocalMachine</c> is passed,
/// and a machine-scoped one opens when <c>CurrentUser</c> is, because the
/// scope travels inside the blob and Windows uses whichever key the blob names.
/// Nothing here relies on that argument for anything. What stops another
/// account reading a user-scoped blob is that it does not have the key —
/// verified by protecting under one scope and opening it under the other,
/// which succeeds and would be alarming if the flag meant what it looks like
/// it means.
/// </para>
///
/// <para>
/// <b>The entropy is not a secret.</b> It is a fixed string compiled into
/// Patchbay, and anyone with the binary has it. It buys one thing: another
/// program running as the same user cannot unprotect a Patchbay blob by
/// handing it to DPAPI and seeing what falls out — it has to be written
/// against Patchbay specifically. It is a speed bump, and calling it anything
/// more would be dishonest. Nothing here defends against code running as the
/// signed-in user, because DPAPI's whole contract is to serve that user.
/// </para>
///
/// <para>
/// Lives in the shell rather than in <c>Core</c> because <c>Core</c> is
/// platform-neutral and <c>ArchitectureTests</c> enforces it. Everything about
/// this that could be got wrong in an interesting way — the envelope, the
/// version and scheme checks, the order of them — is in
/// <see cref="SecretProtector"/> instead, where the tests can reach it. What
/// is left here is two calls to Windows.
/// </para>
/// </summary>
public sealed class DpapiSecretProtector : SecretProtector
{
    /// <summary>
    /// The name that goes in the file. Short, and it names the mechanism
    /// rather than the product, so a Credential Manager blob (M3-04) sitting
    /// beside it in the same document is obviously a different thing.
    /// </summary>
    public const string SchemeName = "dpapi";

    /// <summary>
    /// Additional entropy. Fixed, so that a blob written by any Patchbay can
    /// be read by any other on the same account, which is what makes an
    /// upgrade not lose everyone's passwords.
    /// </summary>
    private static readonly byte[] ApplicationEntropy =
        Encoding.UTF8.GetBytes("Patchbay/secret-protection/v1");

    private bool? _available;

    /// <inheritdoc />
    public override string Scheme => SchemeName;

    /// <summary>
    /// Whether DPAPI actually works for this account, established by using it
    /// rather than by assuming it.
    ///
    /// <para>
    /// Being on Windows is not the same as having working data protection: an
    /// account whose profile has not been loaded — a service account, a
    /// scheduled task, some remote-execution contexts — has no master key, and
    /// the failure arrives as a <see cref="CryptographicException"/> at the
    /// moment someone tries to save a password. Finding that out with a
    /// two-byte round trip on first use means the offer to save one is never
    /// made in the first place.
    /// </para>
    /// </summary>
    public override bool IsAvailable => _available ??= SelfTest();

    /// <summary>
    /// The protector to use on this machine: this one when it works, and one
    /// that refuses when it does not. The choice is made once, here, so that
    /// no caller has to remember that saving a password can be impossible.
    /// </summary>
    public static ISecretProtector ForCurrentUser()
    {
        DpapiSecretProtector protector = new();

        return protector.IsAvailable ? protector : UnavailableSecretProtector.Instance;
    }

    /// <inheritdoc />
    protected override byte[] ProtectCore(string secret)
    {
        byte[] plain = Encoding.UTF8.GetBytes(secret);

        try
        {
            return ProtectedData.Protect(plain, ApplicationEntropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new SecretProtectionException(
                "Windows would not protect this password, so Patchbay has not saved it. "
                + "The connection can still ask for it each time it connects.",
                ex);
        }
        finally
        {
            // The copy this method made, gone before it is garbage. The string
            // it came from is not ours to clear and outlives this call; that
            // is M3-03's problem, and it needs a different API to solve.
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <inheritdoc />
    protected override SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload)
    {
        byte[]? plain = null;

        try
        {
            plain = ProtectedData.Unprotect(
                payload.ToArray(), ApplicationEntropy, DataProtectionScope.CurrentUser);

            return SecretUnprotectResult.Success(Encoding.UTF8.GetString(plain));
        }
        catch (CryptographicException)
        {
            // Another account, another machine, or an altered blob. DPAPI does
            // not say which and it does not matter: all three mean this
            // password has to be typed again here.
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
        }
        finally
        {
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }

    private static bool SelfTest()
    {
        byte[] probe = [0x50, 0x62];

        try
        {
            byte[] wrapped = ProtectedData.Protect(
                probe, ApplicationEntropy, DataProtectionScope.CurrentUser);

            byte[] opened = ProtectedData.Unprotect(
                wrapped, ApplicationEntropy, DataProtectionScope.CurrentUser);

            return opened.AsSpan().SequenceEqual(probe);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
