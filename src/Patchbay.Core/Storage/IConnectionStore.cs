using Patchbay.Core.Model;

namespace Patchbay.Core.Storage;

/// <summary>
/// Reads and writes the connection document. Abstracted so the view models
/// can be tested against an in-memory store, and so an alternative backing
/// store (a network share, a team file — M8-07) can be dropped in later.
/// </summary>
public interface IConnectionStore
{
    /// <summary>Where the document lives. Shown in the title bar and in errors.</summary>
    string FilePath { get; }

    /// <summary>Whether a document is already present at <see cref="FilePath"/>.</summary>
    bool Exists { get; }

    /// <summary>
    /// Loads the document, creating an empty one if none exists and falling
    /// back to a backup if the main file cannot be read. Inspect the returned
    /// <see cref="LoadResult"/> to find out which of those happened — it
    /// matters, and the person needs telling.
    /// </summary>
    /// <exception cref="Serialization.ConnectionDocumentException">
    /// The document and every backup are unreadable.
    /// </exception>
    Task<LoadResult> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the document. The file on disk is either the old contents or the
    /// new ones — never a half-written mixture, even if the process dies
    /// during the call.
    /// </summary>
    Task SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many previous versions of the document are sitting beside it.
    ///
    /// <para>
    /// Worth asking about because they inherit the protection the document had
    /// when they were written, not the protection it has now. Turning on a
    /// master password (M3-07) leaves every one of them holding passwords
    /// under the old scheme, and somebody who has just protected their
    /// document is entitled to be told that.
    /// </para>
    /// </summary>
    int OlderCopies { get; }

    /// <summary>
    /// Deletes the previous versions. Returns how many went.
    ///
    /// <para>
    /// Destructive and deliberately not automatic. Backups are what recovers a
    /// document from a bad save, and the moment just after changing how it is
    /// protected is a poor one to be without them — so this is a thing to
    /// choose, not a thing that happens.
    /// </para>
    /// </summary>
    int ForgetOlderCopies();
}
