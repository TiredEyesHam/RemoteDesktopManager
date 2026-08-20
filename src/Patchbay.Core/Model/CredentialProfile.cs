using System.Text.Json.Serialization;

namespace Patchbay.Core.Model;

/// <summary>
/// A named sign-in that any number of connections can point at (M3-01).
///
/// The point of a profile is that the same account is usually used for a lot
/// of machines, and a password change should be one edit rather than fifty.
/// Nodes refer to it by <see cref="ConnectionSettings.CredentialProfileId"/>,
/// which is inheritable like every other setting, so a group can name the
/// account its servers use.
///
/// <see cref="ProtectedPassword"/> is the only place a stored password lives,
/// and it is never plaintext. It holds the envelope text that
/// <c>ISecretProtector</c> produced, which names the scheme that wrote it, so
/// a document can carry blobs from more than one store and a blob nothing here
/// can open is left alone rather than destroyed. Nothing in this type can
/// protect or unprotect anything; <c>CredentialVault</c> owns that, and owns
/// the only reference to a protector.
/// </summary>
public sealed class CredentialProfile
{
    /// <summary>What connections point at. Stable for the life of the profile.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>What a person picks in a list. Not unique and not an identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The account to sign in as.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>The domain that goes with it, or empty for a local account.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The saved password as a protected envelope, or null when none is saved.
    ///
    /// Settable because the serialiser needs it to be. Everything that writes
    /// it goes through <c>CredentialVault</c>, so that a plaintext password
    /// cannot be assigned here by accident.
    /// </summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>Whether there is a saved password at all. Says nothing about whether it can be read.</summary>
    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(ProtectedPassword);

    /// <summary>The account as a person writes it. Never includes the password.</summary>
    [JsonIgnore]
    public string Display => Domain.Length > 0 && UserName.Length > 0
        ? Domain + "\\" + UserName
        : UserName;

    /// <summary>What to show in a list: the name, and the account it signs in as.</summary>
    [JsonIgnore]
    public string Label => Name.Length > 0 && Display.Length > 0
        ? $"{Name} ({Display})"
        : Name.Length > 0 ? Name : Display;

    /// <summary>
    /// Copies everything except the id, so a duplicate is a new profile rather
    /// than a second reference to the same one. The protected password travels
    /// with it: it is already protected, and re-protecting would need the
    /// plaintext this type never has.
    /// </summary>
    public CredentialProfile CloneAsNew() => new()
    {
        Name = Name,
        UserName = UserName,
        Domain = Domain,
        ProtectedPassword = ProtectedPassword,
    };

    /// <summary>Redacted, so a profile in a log line cannot carry an envelope with it.</summary>
    public override string ToString()
        => $"{nameof(CredentialProfile)} {{ {Label}, password {(HasPassword ? "saved" : "none")} }}";
}
