namespace Patchbay.Core.Security;

/// <summary>
/// A secret store that keeps the secrets somewhere other than the document
/// (M3-04), and can therefore end up holding ones the document has forgotten
/// about.
///
/// <para>
/// Every store before this one put the ciphertext in the file, which made the
/// file the only record: delete the field and the secret is gone. Windows
/// Credential Manager holds the password and the document holds a name, and
/// two things that can change independently will. A document restored from a
/// backup refers to entries a later version already released; a document
/// deleted outside Patchbay refers to nothing at all and its entries stay; a
/// crash between writing an entry and saving the document leaves one nobody
/// will ever ask for again.
/// </para>
///
/// <para>
/// None of those is dangerous and all of them are visible: they pile up in the
/// Windows Credential Manager control panel under Patchbay's name, where the
/// reasonable conclusion is that Patchbay is leaking passwords. So the store
/// can be asked what it holds and told what is still wanted — and both are
/// things somebody chooses rather than things that happen quietly, because
/// deleting a password because a document did not mention it is precisely the
/// wrong move when the document being consulted is the wrong one.
/// </para>
///
/// <para>
/// <b>Which is why the store is opened against a document.</b> Patchbay opens
/// one document at a time but a person may have several, and every one of them
/// keeps its entries in the same Windows store. A sweep that deleted every
/// Patchbay entry the open document did not name would delete the other
/// document's passwords, silently, as a tidying-up operation. Entries are
/// therefore filed under the document they belong to and a sweep never sees
/// past it — which errs towards leaving an orphan behind rather than towards
/// destroying a password, and that is the correct direction to be wrong in.
/// </para>
/// </summary>
public interface IExternalSecretStore
{
    /// <summary>
    /// Points the store at a document. Everything written from now on is filed
    /// under it, and <see cref="Count"/> and <see cref="ForgetOrphans"/> see
    /// nothing else.
    /// </summary>
    void Open(Guid documentId);

    /// <summary>
    /// How many entries the store holds for the open document, whether or not
    /// anything still refers to them. Zero when the store is unavailable —
    /// not being able to look is not the same as there being none, but there
    /// is nothing useful to offer either way.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Deletes every entry belonging to the open document that
    /// <paramref name="inUse"/> does not name, and returns how many went.
    /// </summary>
    /// <param name="inUse">
    /// The stored values still being referred to — envelopes exactly as they
    /// appear in the document. Anything that is not one of this store's
    /// envelopes is ignored rather than refused, so the caller can hand over
    /// every protected password it has without sorting them by scheme first.
    /// </param>
    int ForgetOrphans(IEnumerable<string?> inUse);
}
