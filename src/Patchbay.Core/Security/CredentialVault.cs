using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Core.Security;

/// <summary>
/// The only thing that turns a saved profile into a sign-in, and the only
/// thing that puts a password into one (M3-01).
///
/// Both directions live here so that the protector has exactly one caller.
/// Spreading protect and unprotect around the application is how a plaintext
/// password ends up assigned straight to
/// <see cref="CredentialProfile.ProtectedPassword"/> by a screen that meant
/// well, and nothing about the resulting document looks wrong until somebody
/// opens it in a text editor.
///
/// Reading never throws. A missing profile and an unreadable password are
/// ordinary outcomes of opening a file that has moved between accounts, and
/// both come back as a <see cref="CredentialResolution"/> that still carries
/// whatever account name is known. Writing does throw, following M3-02: a
/// failed protect must not fall back to storing plaintext, because nothing on
/// screen would change and the only difference would be a password in a file
/// that gets backed up.
/// </summary>
public sealed class CredentialVault
{
    private readonly ISecretProtector _protector;

    public CredentialVault(ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    /// <summary>
    /// Whether passwords can be saved at all here. False on an account with no
    /// working data protection, where the offer to save one should never be
    /// made rather than made and then refused.
    /// </summary>
    public bool CanSavePasswords => _protector.IsAvailable;

    /// <summary>
    /// Works out the sign-in for a connection from its resolved settings.
    /// </summary>
    /// <param name="document">The document the profile would be in.</param>
    /// <param name="settings">
    /// Settings that have already been through
    /// <c>SettingsResolver</c>, so that a profile named on a group is seen by
    /// its servers.
    /// </param>
    public CredentialResolution Resolve(ConnectionDocument document, ConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        CredentialMode mode = settings.CredentialMode ?? CredentialMode.Prompt;

        if (mode is CredentialMode.CurrentUser)
        {
            // Windows supplies it. Naming an account here would be asking for
            // a logon prompt rather than avoiding one.
            return new CredentialResolution
            {
                Status = CredentialResolutionStatus.SingleSignOn,
                Credentials = SessionCredentials.None,
            };
        }

        if (mode is CredentialMode.Prompt)
        {
            // Ask every time (M3-05). The account name on the node fills the
            // box in; the mapper falls back to it anyway if nobody types one
            // (M4-10).
            return new CredentialResolution
            {
                Status = CredentialResolutionStatus.AskEveryTime,
                Credentials = new SessionCredentials
                {
                    UserName = settings.UserName ?? string.Empty,
                    Domain = settings.Domain ?? string.Empty,
                },
            };
        }

        // Configured to use a profile and not told which. Same outcome as one
        // that has been deleted, because the same thing has to happen next.
        if (settings.CredentialProfileId is not { } id)
        {
            return Missing();
        }

        if (document.FindCredential(id) is not { } profile)
        {
            return Missing();
        }

        SessionCredentials account = new()
        {
            UserName = profile.UserName,
            Domain = profile.Domain,
        };

        if (!profile.HasPassword)
        {
            return new CredentialResolution
            {
                Status = CredentialResolutionStatus.Resolved,
                Credentials = account,
                Profile = profile,
            };
        }

        SecretUnprotectResult opened = _protector.Unprotect(profile.ProtectedPassword);

        if (!opened.IsSuccess || opened.Secret is not { } password)
        {
            return new CredentialResolution
            {
                Status = CredentialResolutionStatus.PasswordUnreadable,
                Credentials = account,
                Profile = profile,
            };
        }

        return new CredentialResolution
        {
            Status = CredentialResolutionStatus.Resolved,
            Credentials = account with { Password = password },
            Profile = profile,
        };

        static CredentialResolution Missing() => new()
        {
            Status = CredentialResolutionStatus.ProfileMissing,
            Credentials = SessionCredentials.None,
        };
    }

    /// <summary>
    /// Protects <paramref name="password"/> and stores it on the profile.
    /// </summary>
    /// <exception cref="SecretProtectionException">
    /// Protection is unavailable or failed. The profile is left untouched, so
    /// a password that was already saved survives a failed attempt to replace
    /// it.
    /// </exception>
    public void SavePassword(CredentialProfile profile, string password)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrEmpty(password);

        // Protected first, assigned second. The other order leaves the profile
        // holding a half-written value when protection throws.
        string envelope = _protector.Protect(password);

        profile.ProtectedPassword = envelope;
    }

    /// <summary>
    /// Forgets the saved password, leaving the account name and domain alone.
    /// Safe to call on a profile that has none.
    /// </summary>
    public static void ClearPassword(CredentialProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.ProtectedPassword = null;
    }
}
