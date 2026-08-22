using System.Globalization;
using System.Security.Cryptography;
using Patchbay.App.Interop;
using Patchbay.Core.Security;

namespace Patchbay.App.Security;

/// <summary>
/// Keeps saved passwords in Windows Credential Manager instead of in the
/// connection document (M3-04).
///
/// <para>
/// <b>What it changes, and what it does not.</b> A Credential Manager entry is
/// protected by the same Windows data protection a
/// <see cref="DpapiSecretProtector"/> blob is, so this is not stronger
/// cryptography and does not defend against anything the other one lets
/// through. What changes is where the ciphertext sits. With DPAPI the document
/// carries it, so a file put on a share, attached to a ticket or committed by
/// accident carries an encrypted password with it — useless to the person who
/// picks it up, and theirs to keep and attack for as long as they like. With
/// this, the document carries a name and the machine keeps the password, and a
/// copy of the file that leaves has no password material in it at all.
/// </para>
///
/// <para>
/// The cost is the mirror image: the document is no longer sufficient. Restore
/// it on a fresh machine and the connections are all there and none of the
/// passwords are, where a DPAPI document at least still holds them for the
/// account that wrote it. That is a real trade and it is why this is offered
/// rather than made the default.
/// </para>
///
/// <para>
/// <b>Entries are filed under the document that owns them.</b> The target name
/// is <c>Patchbay/{document}/{entry}</c>, and both halves earn their place.
/// The document half scopes the sweep — Patchbay opens one file at a time but
/// a person may have several, and a tidy-up that deleted every Patchbay entry
/// the open document did not mention would delete the other document's
/// passwords. The entry half is a fresh identifier per saved password rather
/// than anything derived from the profile, so that nothing about a person's
/// account names or servers appears in a list Windows shows to anybody who
/// opens the control panel.
/// </para>
///
/// <para>
/// <b>The stored value is a name, not a secret.</b> Which is why it is 16
/// bytes and not a target string: a document that carried the whole target
/// name could be edited by hand to point at any credential in the person's
/// store, and Patchbay would read it back and hand it to a server. Sixteen
/// bytes of identifier can only ever name a Patchbay entry belonging to the
/// document holding it.
/// </para>
/// </summary>
public sealed class CredentialManagerSecretProtector : SecretProtector, IExternalSecretStore
{
    /// <summary>
    /// The name that goes in the file. Names the mechanism rather than the
    /// product, like <c>dpapi</c> beside it.
    /// </summary>
    public const string SchemeName = "wincred";

    private const string Prefix = "Patchbay";

    /// <summary>
    /// What Windows shows in the account column. Not a real account: the
    /// protector is handed a password and nothing else, and inventing a user
    /// name from somewhere would put the person's server logins into a list
    /// they did not ask to publish.
    /// </summary>
    private const string ShownAs = "Patchbay saved password";

    private Guid _document;
    private bool? _available;

    /// <inheritdoc />
    public override string Scheme => SchemeName;

    /// <summary>
    /// Whether entries can be written and read here, established by doing it
    /// rather than by assuming it — the same reasoning as
    /// <see cref="DpapiSecretProtector.IsAvailable"/>, and one reason more.
    /// Storing credentials can be turned off by policy, and a machine where it
    /// is off looks exactly like one where it is on until the first write
    /// fails.
    ///
    /// <para>
    /// False until a document has been opened, because an entry has to be
    /// filed under one. That is a moment rather than a state:
    /// <c>DocumentProtection.Open</c> does it before anything asks.
    /// </para>
    /// </summary>
    public override bool IsAvailable => _document != Guid.Empty && (_available ??= SelfTest());

    /// <inheritdoc />
    protected override string UnavailableMessage =>
        "Windows Credential Manager is not storing credentials on this machine, so there is "
        + "nowhere to put this password. It may be turned off by policy.";

    /// <inheritdoc />
    public void Open(Guid documentId) => _document = documentId;

    /// <inheritdoc />
    public int Count => IsAvailable ? WindowsCredentials.Names(Filter).Count : 0;

    /// <inheritdoc />
    public int ForgetOrphans(IEnumerable<string?> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);

        if (!IsAvailable)
        {
            return 0;
        }

        // Everything still spoken for, read out of the envelopes rather than
        // inferred. An envelope belonging to another scheme contributes
        // nothing and is not an error: a mixed document is ordinary, and the
        // caller should not have to sort by scheme to ask this.
        HashSet<Guid> wanted = [];

        foreach (string? stored in inUse)
        {
            if (TryReadEntry(stored, out Guid entry))
            {
                wanted.Add(entry);
            }
        }

        int forgotten = 0;

        foreach (string target in WindowsCredentials.Names(Filter))
        {
            // A name that does not parse is left alone. It is under Patchbay's
            // prefix and this build cannot account for it, which is a reason
            // to be careful rather than a reason to delete.
            if (TryReadTarget(target, out Guid entry)
                && !wanted.Contains(entry)
                && WindowsCredentials.Delete(target))
            {
                forgotten++;
            }
        }

        return forgotten;
    }

    /// <inheritdoc />
    protected override byte[] ProtectCore(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > WindowsCredentials.MaxBlobLength)
        {
            throw new SecretProtectionException(
                "Windows Credential Manager will not store a password this long, so Patchbay "
                + "has not saved it. Windows data protection has no such limit.");
        }

        // A fresh identifier every time, including when a password is being
        // replaced. Writing over an entry in place would save one delete and
        // would mean a failed save had already destroyed the password it was
        // replacing; the old entry is released afterwards, by whoever stopped
        // referring to it.
        Guid entry = Guid.NewGuid();

        if (!WindowsCredentials.TryWrite(TargetFor(entry), utf8, ShownAs))
        {
            throw new SecretProtectionException(
                "Windows would not store this password in Credential Manager, so Patchbay has "
                + "not saved it. The connection can still ask for it each time it connects.");
        }

        return entry.ToByteArray();
    }

    /// <inheritdoc />
    protected override SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload)
    {
        if (!TryReadEntry(payload, out Guid entry))
        {
            // Sixteen bytes or it is not one of these. Somebody has edited the
            // field by hand, which is the one case here that is not simply an
            // entry that has gone.
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
        }

        if (!WindowsCredentials.TryRead(TargetFor(entry), out byte[]? blob) || blob is null)
        {
            // Absent rather than shut, and the difference is the whole of what
            // the person needs to hear: this document has moved, or the entry
            // was removed in Windows.
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.Missing);
        }

        try
        {
            return SecretUnprotectResult.Success(Secret.FromUtf8(blob));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    /// <inheritdoc />
    protected override void ForgetCore(ReadOnlySpan<byte> payload)
    {
        if (TryReadEntry(payload, out Guid entry))
        {
            WindowsCredentials.Delete(TargetFor(entry));
        }
    }

    private string Filter => string.Create(
        CultureInfo.InvariantCulture, $"{Prefix}/{_document:N}/*");

    private string TargetFor(Guid entry) => string.Create(
        CultureInfo.InvariantCulture, $"{Prefix}/{_document:N}/{entry:N}");

    private static bool TryReadEntry(ReadOnlySpan<byte> payload, out Guid entry)
    {
        if (payload.Length != 16)
        {
            entry = Guid.Empty;
            return false;
        }

        entry = new Guid(payload);
        return true;
    }

    private static bool TryReadEntry(string? stored, out Guid entry)
    {
        entry = Guid.Empty;

        return SecretEnvelope.TryParse(stored, out SecretEnvelope? envelope)
            && string.Equals(envelope.Scheme, SchemeName, StringComparison.Ordinal)
            && TryReadEntry(envelope.Payload.Span, out entry);
    }

    private bool TryReadTarget(string target, out Guid entry)
    {
        entry = Guid.Empty;

        string expected = string.Create(CultureInfo.InvariantCulture, $"{Prefix}/{_document:N}/");

        return target.StartsWith(expected, StringComparison.Ordinal)
            && Guid.TryParseExact(target.AsSpan(expected.Length), "N", out entry);
    }

    /// <summary>
    /// A round trip through the real store, under the open document, so that a
    /// probe stranded by a crash is an orphan the sweep clears rather than
    /// something that lingers under a name nothing accounts for. Two bytes
    /// that are not a secret.
    /// </summary>
    private bool SelfTest()
    {
        Guid entry = Guid.NewGuid();
        string target = TargetFor(entry);

        try
        {
            if (!WindowsCredentials.TryWrite(target, [0x50, 0x62], ShownAs))
            {
                return false;
            }

            return WindowsCredentials.TryRead(target, out byte[]? read)
                && read is [0x50, 0x62];
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            WindowsCredentials.Delete(target);
        }
    }
}
