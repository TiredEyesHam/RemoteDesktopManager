using CommunityToolkit.Mvvm.ComponentModel;
using Patchbay.Core.Sessions;

namespace Patchbay.App.ViewModels;

/// <summary>
/// The docked credential panel, bound (M3-06).
///
/// A thin wrapper. Every rule lives in <see cref="CredentialPrompt"/> in
/// <c>Core</c>, where it can be tested without a window; what is here is the
/// change notification a text box needs so that Connect enables itself as
/// somebody types, and disables itself again when what they have typed is what
/// the far end already refused.
/// </summary>
public sealed partial class CredentialPromptViewModel : ObservableObject
{
    private readonly CredentialPrompt _prompt;

    public CredentialPromptViewModel(CredentialPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        _prompt = prompt;
    }

    /// <summary>The rules and the answer, for whoever is going to connect with it.</summary>
    public CredentialPrompt Prompt => _prompt;

    public string Title => _prompt.Title;

    public string? Detail => _prompt.Detail;

    public bool HasDetail => _prompt.Detail is not null;

    public bool CanOfferToSave => _prompt.CanOfferToSave;

    public string UserName
    {
        get => _prompt.UserName;
        set => Set(v => _prompt.UserName = v, value, _prompt.UserName);
    }

    public string Domain
    {
        get => _prompt.Domain;
        set => Set(v => _prompt.Domain = v, value, _prompt.Domain);
    }

    /// <summary>
    /// Bound from a <c>PasswordBox</c> in code-behind rather than by a binding,
    /// because <c>PasswordBox.Password</c> is deliberately not a dependency
    /// property — binding it would park the plaintext in the binding engine,
    /// which is exactly the sort of extra copy M3-03 is about.
    /// </summary>
    public string Password
    {
        get => _prompt.Password;
        set => Set(v => _prompt.Password = v, value, _prompt.Password);
    }

    public bool SavePassword
    {
        get => _prompt.SavePassword;
        set
        {
            _prompt.SavePassword = value;

            // Read back rather than echoed: the prompt refuses to be told yes
            // where saving cannot work, and the checkbox should show that
            // rather than lie about it.
            OnPropertyChanged();
        }
    }

    public bool CanSubmit => _prompt.CanSubmit;

    /// <summary>Why Connect is disabled, or null when it is not.</summary>
    public string? Obstacle => _prompt.Obstacle;

    public bool HasObstacle => _prompt.Obstacle is not null;

    /// <summary>Drops the typed password once it has been handed to an attempt.</summary>
    public void Forget()
    {
        _prompt.Forget();
        OnPropertyChanged(nameof(Password));
        Refresh();
    }

    private void Set(Action<string> assign, string value, string current)
    {
        if (string.Equals(value, current, StringComparison.Ordinal))
        {
            return;
        }

        assign(value ?? string.Empty);
        OnPropertyChanged();
        Refresh();
    }

    /// <summary>
    /// Everything derived from the three boxes at once. Cheap, and the
    /// alternative is remembering which of them affects which, which is how a
    /// Connect button stays greyed out after the thing blocking it was fixed.
    /// </summary>
    private void Refresh()
    {
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(Obstacle));
        OnPropertyChanged(nameof(HasObstacle));
    }
}
