using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Patchbay.Core.Model;
using Patchbay.Core.Security;
using Patchbay.Core.Storage;

namespace Patchbay.App.ViewModels;

/// <summary>
/// The document's master password, as a panel (M3-07).
///
/// <para>
/// One panel for three states, because they are three moments in one thing
/// rather than three features. A document with no master password offers to
/// take one; a locked one asks for it; an open one offers to change or remove
/// it. Splitting them across separate screens would mean somebody looking for
/// "the master password" finding the wrong one of three.
/// </para>
///
/// <para>
/// Every rule is in <see cref="DocumentProtection"/> in <c>Core</c>, where
/// there are tests. What is here is the change notification the buttons need
/// and the discipline about what happens to the typed password afterwards.
/// </para>
/// </summary>
public sealed partial class DocumentSecurityViewModel : ObservableObject
{
    private readonly DocumentProtection _protection;
    private readonly ConnectionDocument _document;
    private readonly IConnectionStore _store;
    private readonly Action _changed;

    private string _password = string.Empty;
    private string _replacement = string.Empty;

    /// <param name="changed">
    /// Saves the document. Called only when something actually changed, which
    /// unlocking never does — the key was already in the file.
    /// </param>
    public DocumentSecurityViewModel(
        DocumentProtection protection,
        ConnectionDocument document,
        IConnectionStore store,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(changed);

        _protection = protection;
        _document = document;
        _store = store;
        _changed = changed;
    }

    /// <summary>
    /// Asks the window to empty its password boxes.
    ///
    /// <para>
    /// A <c>PasswordBox</c> keeps its own copy of what was typed, and clearing
    /// the view model's string does nothing about it. For an ordinary password
    /// that would be a cosmetic loose end; for the one password that opens
    /// every other password in the document, leaving it live in a control for
    /// as long as a panel happens to be on screen is not.
    /// </para>
    /// </summary>
    public event EventHandler? Emptied;

    /// <summary>Whether this document has a master password at all.</summary>
    public bool IsProtected => _protection.IsProtected;

    /// <summary>Whether it has one and nobody has typed it yet.</summary>
    public bool NeedsUnlocking => _protection.NeedsUnlocking;

    /// <summary>Whether it has one and the key is in hand.</summary>
    public bool IsOpen => _protection.IsProtected && _protection.IsUnlocked;

    /// <summary>Whether the offer to set one should be on screen.</summary>
    public bool CanProtect => !_protection.IsProtected;

    /// <summary>
    /// What the panel says about where things stand. The first thing somebody
    /// opening it wants, and the sentence they will quote when asking whether
    /// the file is safe to put on a share.
    /// </summary>
    public string StateText => _protection switch
    {
        { IsProtected: false, CanUseMachineProtection: true } =>
            "Saved passwords are protected for this Windows account on this machine. Anyone "
            + "signed in as you, and any local administrator, can read them.",
        { IsProtected: false } =>
            "Windows data protection is not working for this account, so passwords cannot be "
            + "saved at all. A master password would give them somewhere to go.",
        { IsUnlocked: false } =>
            "This document has a master password. The saved passwords in it cannot be read "
            + "until it is entered.",
        _ =>
            "This document has a master password, and it is open. The saved passwords in it "
            + "are readable on any machine by anyone who knows it.",
    };

    /// <summary>
    /// What the first box is asking for, which is a different thing in each
    /// of the three states and the same box in all of them.
    /// </summary>
    public string PasswordLabel => _protection switch
    {
        { IsProtected: false } => "New master password",
        { IsUnlocked: false } => "Master password",
        _ => "Current master password",
    };

    /// <summary>
    /// The length rule, shown only where it applies. A rule stated while
    /// somebody is unlocking is noise; stated while they are choosing one, it
    /// is the only thing they need.
    /// </summary>
    public string LengthHint { get; } =
        $"At least {DocumentProtection.MinimumPasswordLength} characters. "
        + "There is no way to recover a document whose master password is forgotten.";

    /// <summary>
    /// The master password being typed — the current one when unlocking,
    /// changing or removing, and the new one when setting.
    ///
    /// <para>
    /// Pushed from a <c>PasswordBox</c> in code-behind rather than bound, for
    /// the same reason as the docked prompt's: <c>PasswordBox.Password</c> is
    /// deliberately not a dependency property, and binding it would park the
    /// plaintext in the binding engine (M3-03).
    /// </para>
    /// </summary>
    public string Password
    {
        get => _password;
        set
        {
            _password = value ?? string.Empty;
            OnPropertyChanged();
            Refresh();
        }
    }

    /// <summary>The replacement, when changing one.</summary>
    public string Replacement
    {
        get => _replacement;
        set
        {
            _replacement = value ?? string.Empty;
            OnPropertyChanged();
            Refresh();
        }
    }

    /// <summary>
    /// Whether there are older copies of this document beside it that a master
    /// password does not cover.
    ///
    /// <para>
    /// The store keeps five previous versions (M1-07), and each of them
    /// carries the protection the document had when it was written rather than
    /// the protection it has now. Turning on a master password therefore
    /// protects the document and not the copies of it — verified rather than
    /// assumed: a backup written before the change hands its passwords back
    /// under Windows data protection alone. Somebody who has just protected a
    /// document is entitled to know that before they conclude the file is safe
    /// to put on a share.
    /// </para>
    /// </summary>
    public bool ShowOlderCopies => _protection.IsProtected && _store.OlderCopies > 0;

    /// <summary>What there is to say about them.</summary>
    public string OlderCopiesText
    {
        get
        {
            int copies = _store.OlderCopies;

            return copies == 1
                ? "One older copy of this document is kept beside it. It was written before the "
                    + "master password, and the sign-ins saved in it can still be read without one."
                : $"{copies} older copies of this document are kept beside it. They were written "
                    + "before the master password, and the sign-ins saved in them can still be "
                    + "read without one.";
        }
    }

    /// <summary>What the button offers, which has to name what it destroys.</summary>
    public string ForgetOlderCopiesLabel =>
        _store.OlderCopies == 1 ? "Delete the older copy" : "Delete the older copies";

    /// <summary>What just happened, or null before anything has.</summary>
    [ObservableProperty]
    private string? notice;

    /// <summary>
    /// Opens the document. Nothing is written: the key was already in the
    /// file, and unlocking only means it is now in hand.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPassword))]
    private void Unlock()
    {
        MasterKeyStatus status = _protection.Unlock(Password);

        Empty();

        Notice = status == MasterKeyStatus.Unlocked
            ? "The document is open."
            : MasterKeyResult.NoticeFor(status);

        Refresh();
    }

    /// <summary>Puts a master password on a document that has none.</summary>
    [RelayCommand(CanExecute = nameof(HasPassword))]
    private void Protect()
    {
        Apply(() => _protection.Set(_document, Password));
    }

    /// <summary>Replaces it, which moves no saved passwords.</summary>
    [RelayCommand(CanExecute = nameof(CanChange))]
    private void Change()
    {
        Apply(() => _protection.Change(_document, Password, Replacement));
    }

    /// <summary>
    /// Takes it off, putting the saved passwords back into machine
    /// protection. Asks for the current one even though the document is
    /// already open, because somebody who walked up to an unlocked screen
    /// should not be able to undo this without knowing it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPassword))]
    private void StopProtecting()
    {
        Apply(() => _protection.Remove(_document, Password));
    }

    /// <summary>
    /// Throws the previous versions away. Offered rather than done, because
    /// backups are what recovers a document from a bad save and the moment
    /// just after changing how it is protected is a poor one to have none.
    /// </summary>
    [RelayCommand]
    private void ForgetOlderCopies()
    {
        int gone = _store.ForgetOlderCopies();

        Notice = gone == 1
            ? "The older copy has been deleted."
            : $"{gone} older copies have been deleted.";

        Refresh();
    }

    private bool HasPassword() => Password.Length > 0;

    private bool CanChange() => Password.Length > 0 && Replacement.Length > 0;

    /// <summary>
    /// Runs one of the three that can change the document, empties the boxes
    /// whether it worked or not, and saves when it did.
    /// </summary>
    private void Apply(Func<MasterPasswordChange> change)
    {
        MasterPasswordChange result = change();

        Empty();

        Notice = result.Notice;

        if (result.IsSuccess)
        {
            // The master key lives in the document, so a change that is not
            // written is a change that did not happen — and on the next open
            // the passwords would be encrypted with a key the file no longer
            // names.
            _changed();
        }

        Refresh();
    }

    private void Empty()
    {
        Password = string.Empty;
        Replacement = string.Empty;

        Emptied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Everything derived from the state and the boxes, at once. The
    /// alternative is remembering which of six properties each of four
    /// commands moves, which is how a button stays greyed out after the thing
    /// blocking it was fixed.
    /// </summary>
    private void Refresh()
    {
        OnPropertyChanged(nameof(IsProtected));
        OnPropertyChanged(nameof(NeedsUnlocking));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(CanProtect));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(PasswordLabel));
        OnPropertyChanged(nameof(ShowOlderCopies));
        OnPropertyChanged(nameof(OlderCopiesText));
        OnPropertyChanged(nameof(ForgetOlderCopiesLabel));

        UnlockCommand.NotifyCanExecuteChanged();
        ProtectCommand.NotifyCanExecuteChanged();
        ChangeCommand.NotifyCanExecuteChanged();
        StopProtectingCommand.NotifyCanExecuteChanged();
    }
}
