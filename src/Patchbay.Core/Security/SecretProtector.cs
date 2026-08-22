namespace Patchbay.Core.Security;

/// <summary>
/// Everything about protecting a secret that is not the platform call
/// (M3-02).
///
/// The envelope, the version check, the scheme check and the order they happen
/// in are all decisions that can be wrong, and all of them are the same for
/// every store Patchbay will ever have. They live here, in <c>Core</c>, where
/// there are tests. What is left for a subclass is two methods that do nothing
/// but hand bytes to the operating system and take them back — which is the
/// part that cannot be tested without the operating system, and so is the part
/// worth making as small as possible.
///
/// <para>
/// The checks run in the order they do because each one makes the next
/// meaningful: text that is not an envelope has no version, a version that is
/// not understood makes the scheme unreadable, and a scheme belonging to
/// somebody else must not be handed to this protector's key. Reordering them
/// turns "saved by a newer Patchbay" into "corrupt".
/// </para>
/// </summary>
public abstract class SecretProtector : ISecretProtector
{
    /// <inheritdoc />
    public abstract string Scheme { get; }

    /// <inheritdoc />
    public virtual bool IsAvailable => true;

    /// <summary>
    /// What to say when <see cref="IsAvailable"/> is false and somebody tried
    /// to save anyway.
    ///
    /// <para>
    /// Overridable because the reason differs and the difference is the whole
    /// message. A machine with no working data protection cannot be made to
    /// have any, and the sentence is about giving up on saving passwords; a
    /// document with a master password nobody has typed yet is one keystroke
    /// from working, and telling that person their machine lacks a secret
    /// store would send them somewhere useless (M3-07).
    /// </para>
    /// </summary>
    protected virtual string UnavailableMessage =>
        $"The '{Scheme}' secret store is not available on this machine, so there is "
        + "nowhere safe to put this password.";

    /// <summary>
    /// What a read reports when <see cref="IsAvailable"/> is false. Paired
    /// with <see cref="UnavailableMessage"/> so that the two cannot drift into
    /// saying different things about the same state.
    /// </summary>
    protected virtual SecretUnprotectStatus UnavailableStatus => SecretUnprotectStatus.Unavailable;

    /// <inheritdoc />
    public string Protect(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (secret.IsEmpty)
        {
            throw new ArgumentException(
                "There is no password here to protect.",
                nameof(secret));
        }

        if (!IsAvailable)
        {
            throw new SecretProtectionException(UnavailableMessage);
        }

        byte[]? payload = null;

        // The bytes are lent for the length of the call and not handed over,
        // so a protector cannot keep the plaintext by accident (M3-03).
        secret.Reveal(this, (utf8, self) => payload = self.ProtectCore(utf8));

        return SecretEnvelope.Create(Scheme, payload!).ToString();
    }

    /// <inheritdoc />
    public SecretUnprotectResult Unprotect(string? storedText)
    {
        if (!SecretEnvelope.TryParse(storedText, out SecretEnvelope? envelope))
        {
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.NotASecret);
        }

        if (envelope.Version > SecretEnvelope.CurrentVersion)
        {
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.TooNew);
        }

        if (!string.Equals(envelope.Scheme, Scheme, StringComparison.Ordinal))
        {
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.WrongScheme);
        }

        if (!IsAvailable)
        {
            return SecretUnprotectResult.Failed(UnavailableStatus);
        }

        return UnprotectCore(envelope.Payload.Span);
    }

    /// <inheritdoc />
    public void Forget(string? storedText)
    {
        // The same checks as a read and in the same order, because the
        // question is the same one: is this a blob of mine? Deleting on a
        // looser test than reading is how a scheme forgets somebody else's
        // secret, and unlike a bad read a bad delete cannot be taken back.
        if (!SecretEnvelope.TryParse(storedText, out SecretEnvelope? envelope)
            || envelope.Version > SecretEnvelope.CurrentVersion
            || !string.Equals(envelope.Scheme, Scheme, StringComparison.Ordinal)
            || !IsAvailable)
        {
            return;
        }

        ForgetCore(envelope.Payload.Span);
    }

    /// <summary>
    /// Protects the secret and returns the payload to put in the envelope.
    /// Called only when <see cref="IsAvailable"/>.
    /// </summary>
    /// <exception cref="SecretProtectionException">The platform refused.</exception>
    protected abstract byte[] ProtectCore(ReadOnlySpan<byte> utf8);

    /// <summary>
    /// Reads a payload this protector wrote. Returns
    /// <see cref="SecretUnprotectStatus.Unreadable"/> rather than throwing when
    /// the platform will not open it — a blob from another account is expected,
    /// not exceptional.
    /// </summary>
    protected abstract SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Releases whatever this payload names outside the document. Nothing by
    /// default, which is right for every store that puts the ciphertext in the
    /// document and wrong for exactly one (M3-04).
    ///
    /// <para>
    /// Must not throw. It is called while tidying up after something that has
    /// already succeeded — a password replaced, a profile deleted — and
    /// turning a completed change into an exception because a stale entry
    /// could not be removed would undo the wrong half of it.
    /// </para>
    /// </summary>
    protected virtual void ForgetCore(ReadOnlySpan<byte> payload)
    {
    }
}
