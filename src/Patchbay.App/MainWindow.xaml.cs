using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Patchbay.App.Interop;
using Patchbay.App.Theme;
using Patchbay.App.ViewModels;

namespace Patchbay.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;

        InitializeComponent();

        DataContext = shell;
        shell.ThemeChanged += (_, _) => ApplyTitleBarTheme();

        SourceInitialized += (_, _) => ApplyTitleBarTheme();

        // Sessions hold a socket and a decoder each, and a hosted control
        // outlives its window unless something ends it.
        Closed += (_, _) => shell.Dispose();
    }

    private void ApplyTitleBarTheme() =>
        WindowTheming.SetDarkTitleBar(this, ThemeManager.Resolved is AppTheme.Dark);

    /// <summary>
    /// TreeView.SelectedItem is read-only, so selection cannot be bound. Three
    /// lines of code-behind is the honest answer; the usual alternative is an
    /// attached behaviour that does the same thing with more machinery.
    /// </summary>
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _shell.SelectedNode = e.NewValue as NodeViewModel;

    /// <summary>
    /// Selects the row under the pointer before its context menu opens.
    /// Without this, right-clicking a row acts on whatever was selected
    /// before, which is how people delete the wrong machine.
    /// </summary>
    private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TreeViewItem item)
            {
                item.IsSelected = true;
                item.Focus();
                return;
            }
        }
    }

    /// <summary>
    /// Double-clicking a machine connects it. On a group the default handling
    /// stands, which is to expand or collapse the row.
    /// </summary>
    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_shell.SelectedNode is { IsServer: true }
            && _shell.ConnectSelectedCommand.CanExecute(null))
        {
            _shell.ConnectSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Carries the typed password from the box to the prompt (M3-06).
    ///
    /// Code-behind because <see cref="PasswordBox.Password"/> is deliberately
    /// not a dependency property: binding it would keep the plaintext in the
    /// binding engine for as long as the panel is on screen, which is one more
    /// copy than there needs to be (M3-03). Pushing it across on each
    /// keystroke also keeps <c>CanSubmit</c> honest, so the button enables
    /// itself as somebody types and disables again if they retype what was
    /// just refused.
    /// </summary>
    private void OnPromptPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: CredentialPromptViewModel prompt } box)
        {
            prompt.Password = box.Password;
        }
    }

    private void OnConnectionsTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Left)
        {
            _shell.ShowConnectionsCommand.Execute(null);
        }
    }

    /// <summary>
    /// Left click brings a tab forward, middle click closes it. Two buttons,
    /// one handler: the alternative is a pair of input bindings that both fire
    /// on the close button as well, and then argue about which won.
    /// </summary>
    private void OnSessionTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SessionTabViewModel tab })
        {
            return;
        }

        switch (e.ChangedButton)
        {
            case MouseButton.Left:
                _shell.ActivateTabCommand.Execute(tab);
                break;

            case MouseButton.Middle:
                // Handled, or the click carries on to whatever ends up under
                // the pointer once the tab has gone.
                _shell.CloseTabCommand.Execute(tab);
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key is Key.F && e.KeyboardDevice.Modifiers is ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key is Key.Escape)
        {
            if (_shell.CancelDeleteCommand.CanExecute(null))
            {
                _shell.CancelDeleteCommand.Execute(null);
            }

            if (_shell.IsEditing)
            {
                _shell.CancelEditorCommand.Execute(null);
                e.Handled = true;
            }
        }

        base.OnPreviewKeyDown(e);
    }
}
