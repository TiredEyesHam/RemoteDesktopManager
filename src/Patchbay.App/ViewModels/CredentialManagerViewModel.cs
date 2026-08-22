using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Patchbay.Core.Editing;
using Patchbay.Core.Model;
using Patchbay.Core.Security;

namespace Patchbay.App.ViewModels;

/// <summary>
/// One row in the saved sign-in list, and the boxes for editing it (M3-10).
///
/// Edits the profile in place rather than into a draft, unlike the connection
/// editor. A profile is four fields with no inheritance and nothing to
/// validate against a tree, so a draft and an Apply button would be machinery
/// standing between somebody and a text box. The document is saved as soon as
/// a row loses focus, which is the same bargain the rest of the shell makes.
/// </summary>
public sealed partial class CredentialRowViewModel : ObservableObject
{
    private readonly CredentialProfile _profile;
    private readonly Action _changed;

    public CredentialRowViewModel(CredentialProfile profile, int usedBy, Action changed)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(changed);

        _profile = profile;
        _changed = changed;
        UsedBy = usedBy;
    }

    public CredentialProfile Profile => _profile;

    public Guid Id => _profile.Id;

    /// <summary>How many connections name this one. Wanted before pressing Delete, not after.</summary>
    public int UsedBy { get; }

    public string UsedByText => UsedBy switch
    {
        0 => "Used by nothing",
        1 => "Used by 1 connection",
        _ => $"Used by {UsedBy} connections",
    };

    public string Name
    {
        get => _profile.Name;
        set => Set(v => _profile.Name = v, value, _profile.Name, nameof(Label));
    }

    public string UserName
    {
        get => _profile.UserName;
        set => Set(v => _profile.UserName = v, value, _profile.UserName, nameof(Label));
    }

    public string Domain
    {
        get => _profile.Domain;
        set => Set(v => _profile.Domain = v, value, _profile.Domain, nameof(Label));
    }

    public string Label => _profile.Label;

    /// <summary>Whether there is a password saved. Never what it is.</summary>
    public bool HasPassword => _profile.HasPassword;

    public string PasswordText => HasPassword ? "A password is saved" : "No password saved";

    /// <summary>Redraws what changed when the password was set or cleared elsewhere.</summary>
    public void RefreshPassword()
    {
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(PasswordText));
    }

    private void Set(Action<string> assign, string value, string current, string also)
    {
        if (string.Equals(value, current, StringComparison.Ordinal))
        {
            return;
        }

        assign(value ?? string.Empty);
        OnPropertyChanged();
        OnPropertyChanged(also);
        _changed();
    }
}

/// <summary>
/// The list of saved sign-ins, and what can be done to it (M3-10).
///
/// <para>
/// Setting a password is the one thing here that reaches the vault, and it is
/// deliberately one-way: a password can be replaced or forgotten and never
/// read back, so this screen cannot show somebody the password they saved last
/// month. That is not an oversight to be fixed later. A manager that can
/// display stored passwords is a manager that will be asked to, by whoever is
/// standing behind the person using it.
/// </para>
/// </summary>
public sealed partial class CredentialManagerViewModel : ObservableObject
{
    private readonly ConnectionDocument _document;
    private readonly CredentialVault _vault;
    private readonly Action _changed;

    public CredentialManagerViewModel(
        ConnectionDocument document,
        CredentialVault vault,
        Action changed)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(changed);

        _document = document;
        _vault = vault;
        _changed = changed;

        Reload();
    }

    public ObservableCollection<CredentialRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CredentialRowViewModel? _selected;

    /// <summary>What is typed into the new-password box. Never pre-filled.</summary>
    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string? _notice;

    public bool HasSelection => Selected is not null;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Whether saving a password can work here at all (M3-02).</summary>
    public bool CanSavePasswords => _vault.CanSavePasswords;

    [RelayCommand]
    private void Add()
    {
        CredentialProfile profile = CredentialOperations.Add(_document);
        _changed();
        Reload();

        Selected = Rows.FirstOrDefault(r => r.Id == profile.Id);
        Notice = null;
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is not { } row)
        {
            return;
        }

        CredentialProfile copy = CredentialOperations.Duplicate(_document, row.Profile);
        _changed();
        Reload();

        Selected = Rows.FirstOrDefault(r => r.Id == copy.Id);
        Notice = null;
    }

    /// <summary>
    /// Deletes the selected sign-in and says what that did to the connections
    /// using it. They are put back to asking each time rather than left
    /// pointing at nothing — see <see cref="CredentialOperations.Delete"/>.
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { } row)
        {
            return;
        }

        CredentialDeletion result = CredentialOperations.Delete(_document, row.Id, _vault);

        if (!result.Deleted)
        {
            return;
        }

        _changed();
        Reload();

        Notice = result.Detached switch
        {
            0 => "Deleted.",
            1 => "Deleted. One connection has been put back to asking each time.",
            _ => $"Deleted. {result.Detached} connections have been put back to asking each time.",
        };
    }

    /// <summary>
    /// Protects what is in the password box and stores it against the selected
    /// sign-in. The box is emptied whether it worked or not, so the plaintext
    /// is not left sitting in a control.
    /// </summary>
    [RelayCommand]
    private void SetPassword()
    {
        if (Selected is not { } row || NewPassword.Length == 0)
        {
            return;
        }

        // Erased on the way out (M3-03). Nothing here connects with it, so
        // this really is the end of the password's life inside Patchbay —
        // apart from the string WPF handed over, which is not ours to clear.
        using Secret password = Secret.From(NewPassword);

        NewPassword = string.Empty;

        try
        {
            _vault.SavePassword(row.Profile, password);
        }
        catch (SecretProtectionException ex)
        {
            Notice = $"The password could not be saved: {ex.Message}";
            return;
        }

        row.RefreshPassword();
        _changed();
        Notice = $"Password saved for {row.Label}.";
    }

    [RelayCommand]
    private void ForgetPassword()
    {
        if (Selected is not { HasPassword: true } row)
        {
            return;
        }

        _vault.ClearPassword(row.Profile);
        row.RefreshPassword();
        _changed();

        Notice = $"Password forgotten for {row.Label}. The account name has been kept.";
    }

    /// <summary>
    /// Rebuilds the rows from the document, keeping whatever was selected if
    /// it is still there. Called after anything that adds or removes one,
    /// because the usage counts belong to the tree and not to the profile.
    /// </summary>
    private void Reload()
    {
        Guid? keep = Selected?.Id;

        Rows.Clear();

        foreach (CredentialProfile profile in _document.Credentials)
        {
            Rows.Add(new CredentialRowViewModel(
                profile,
                _document.NodesUsingCredential(profile.Id).Count(),
                _changed));
        }

        Selected = keep is { } id ? Rows.FirstOrDefault(r => r.Id == id) : null;

        OnPropertyChanged(nameof(IsEmpty));
    }
}
