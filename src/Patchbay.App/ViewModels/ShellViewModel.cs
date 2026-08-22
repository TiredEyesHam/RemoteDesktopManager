using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Patchbay.App.Theme;
using Patchbay.Core.Editing;
using Patchbay.Core.Import;
using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;
using Patchbay.Core.Security;
using Patchbay.Core.Sessions;
using Patchbay.Core.Serialization;
using Patchbay.Core.Storage;

namespace Patchbay.App.ViewModels;

/// <summary>
/// The window's brain: the document, what is selected, what is being edited,
/// and the search box.
///
/// Every change writes the whole document straight back to disk. That is
/// wasteful for a large file and it is deliberate for now — the store's save
/// is atomic, so a save that lands mid-edit cannot corrupt anything, and it
/// means nothing is ever lost by closing the window. Debounced saving over an
/// undo stack is M1-10 and M1-11, and needs the command stack to exist first.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Long enough that typing does not refilter on every keystroke, short
    /// enough that the tree feels attached to the box.
    /// </summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// How often a reconnect countdown is redrawn (M4-08). A second, because
    /// that is the unit it is counted in. Each tab measures how much time has
    /// really passed, so a starved dispatcher makes the display coarse and
    /// never makes the wait wrong.
    /// </summary>
    private static readonly TimeSpan ReconnectTick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ClipboardTick = TimeSpan.FromSeconds(1);

    private readonly IConnectionStore _store;
    private readonly Func<string?>? _chooseImportFile;
    private readonly SecretClipboard _clipboard;
    private readonly Stopwatch _sinceClipboardTick = new();
    private readonly DispatcherTimer _clipboardTimer;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly SessionWorkspace _workspace;
    private readonly DocumentProtection _protection;
    private readonly CredentialVault _credentials;

    private ConnectionDocument _document = new();
    private Action? _afterDiscard;

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private EditorViewModel? _editor;

    /// <summary>
    /// The saved sign-in manager, or null when it is not open (M3-10). Shares
    /// the editor's slot in the window, because both are "a panel over the
    /// details pane" and two of them on screen at once would be two things
    /// editing the same document from different angles.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManagingCredentials))]
    private CredentialManagerViewModel? _credentialManager;

    /// <summary>
    /// The master password panel, or null when it is not open (M3-07). Shares
    /// the slot with the editor and the sign-in manager for the same reason
    /// they share it with each other.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManagingSecurity))]
    private DocumentSecurityViewModel? _documentSecurity;

    [ObservableProperty]
    private bool _isDeleteRequested;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private IReadOnlyList<DetailSection> _detailSections = [];

    [ObservableProperty]
    private bool _hasSearchResults = true;

    /// <summary>What the theme button offers, not what is on screen now.</summary>
    [ObservableProperty]
    private string _themeLabel = string.Empty;

    /// <summary>
    /// The tab on screen, or null for the connections view. A null active tab
    /// is what the permanent first tab means: there is always somewhere to go
    /// back to, so closing the last session never leaves an empty window.
    /// </summary>
    [ObservableProperty]
    private SessionTabViewModel? _activeTab;

    /// <param name="store">Where the document is read from and written to.</param>
    /// <param name="chooseImportFile">
    /// Asks for a file to import, returning null if the person changes their
    /// mind. Supplied by the window: the file dialog is a WPF type, and
    /// reaching for it in here would put a modal box inside the view model.
    /// </param>
    /// <param name="sessionHost">
    /// What opens sessions. Defaults to the fake (M4-01), which connects to
    /// nothing and says so — the shell is built against the interface, so the
    /// real engine is a different argument and nothing else.
    /// </param>
    public ShellViewModel(
        IConnectionStore store,
        Func<string?>? chooseImportFile = null,
        IRemoteSessionHost? sessionHost = null,
        IReadOnlyList<ISecretProtector>? secretStores = null,
        ISystemClipboard? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _chooseImportFile = chooseImportFile;

        // The document's protection, whichever it is using (M3-07, M3-04).
        // Everything downstream holds this rather than a store, so a document
        // behind a master password, one in Windows Credential Manager and one
        // behind DPAPI are the same thing to it.
        //
        // Defaults to the protector that refuses on an account with no working
        // data protection, so a test does not have to reach real DPAPI to
        // build a shell (M3-01).
        _protection = new DocumentProtection(secretStores is null ? [] : [.. secretStores]);
        _credentials = new CredentialVault(_protection);

        // And the same for the clipboard, which is a WPF type (M3-09).
        _clipboard = new SecretClipboard(clipboard ?? UnavailableClipboard.Instance);

        // Delays on the fake, so the connecting state is something that can be
        // seen and got wrong rather than a frame nobody ever draws. The
        // readings are invented for the same reason — a status bar (M5-17)
        // with five dashes in it cannot be looked at and judged. Everything
        // they say is fiction, and the bar says so, in amber, beside them.
        _workspace = new SessionWorkspace(sessionHost ?? new FakeRemoteSessionHost
        {
            ConnectDelay = TimeSpan.FromSeconds(1.2),
            DisconnectDelay = TimeSpan.FromMilliseconds(250),
            SimulatedLatency = TimeSpan.FromMilliseconds(28),
        });

        ThemeLabel = OppositeThemeLabel();

        _searchTimer = new DispatcherTimer { Interval = SearchDelay };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplySearch();
        };

        // Runs only while something is counting down (M4-08). A timer left
        // ticking behind an idle window costs a wake-up a second for nothing,
        // and on a laptop that is a battery cost somebody can measure.
        _reconnectTimer = new DispatcherTimer { Interval = ReconnectTick };
        _reconnectTimer.Tick += OnReconnectTick;

        // Runs only while a password is on the clipboard, for the same reason
        // as the reconnect one. A second is fine: the count is shown to the
        // whole second and the clear is not a deadline anybody is racing.
        _clipboardTimer = new DispatcherTimer { Interval = ClipboardTick };
        _clipboardTimer.Tick += OnClipboardTick;
    }

    /// <summary>Raised when the palette changes, so the window can retheme its title bar.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>A single-item collection, because a TreeView binds to a list.</summary>
    public ObservableCollection<NodeViewModel> Roots { get; } = [];

    /// <summary>The open sessions, in strip order.</summary>
    public ObservableCollection<SessionTabViewModel> Tabs { get; } = [];

    /// <summary>Whether the strip is worth showing. It is not, with nothing open.</summary>
    public bool HasTabs => Tabs.Count > 0;

    /// <summary>True when a session is on screen instead of the connections view.</summary>
    public bool IsSessionVisible => ActiveTab is not null;

    /// <summary>The other half of the swap. Never both.</summary>
    public bool IsBrowsing => ActiveTab is null;

    /// <summary>What is doing the connecting, in words.</summary>
    public string SessionHostDescription => _workspace.HostDescription;

    /// <summary>
    /// True when nothing is really being connected to. Shown on every session,
    /// because a simulated session that looks real is how someone comes to
    /// believe they patched a server they never reached.
    /// </summary>
    public bool IsSimulatedHost => _workspace.IsSimulated;

    public NodeViewModel? Root => Roots.FirstOrDefault();

    public string FilePath => _store.FilePath;

    public string FileName => Path.GetFileName(_store.FilePath);

    public bool HasSelection => SelectedNode is not null;

    public bool IsEditing => Editor is not null;

    public bool IsManagingCredentials => CredentialManager is not null;

    public bool IsManagingSecurity => DocumentSecurity is not null;

    /// <summary>Whether the document is behind a master password nobody has typed (M3-07).</summary>
    public bool IsDocumentLocked => _protection.NeedsUnlocking;

    /// <summary>
    /// Which store this document's saved passwords go to (M3-04). Logged at
    /// startup, because "the password will not save" and "the password saved
    /// and cannot be read" are different problems and this is the line that
    /// tells them apart.
    /// </summary>
    public string SecretStoreScheme => _protection.Scheme;

    /// <summary>Opens the saved sign-in manager, closing any editor first.</summary>
    [RelayCommand]
    private void ManageCredentials()
    {
        Editor = null;
        DocumentSecurity = null;
        CredentialManager = new CredentialManagerViewModel(_document, _credentials, MarkCredentialsChanged);
    }

    [RelayCommand]
    private void CloseCredentials() => CredentialManager = null;

    /// <summary>Opens the master password panel (M3-07).</summary>
    [RelayCommand]
    private void ManageSecurity()
    {
        Editor = null;
        CredentialManager = null;
        DocumentSecurity =
            new DocumentSecurityViewModel(_protection, _document, _store, MarkSecurityChanged);
    }

    [RelayCommand]
    private void CloseSecurity() => DocumentSecurity = null;

    /// <summary>
    /// Writes the document after the master password changed, and refreshes
    /// what depends on it.
    ///
    /// <para>
    /// This save matters more than the others. The wrapped key lives in the
    /// document, so a master password that is set but not written is one that
    /// did not happen — and the saved passwords beside it have already been
    /// re-encrypted with a key the file would no longer name.
    /// </para>
    /// </summary>
    private void MarkSecurityChanged()
    {
        _ = SaveAsync("Document security updated");

        OnPropertyChanged(nameof(IsDocumentLocked));
    }

    /// <summary>
    /// Writes the document after a change to the saved sign-ins, and refreshes
    /// anything showing them.
    ///
    /// Every edit saves, like the rest of the shell. A profile is four fields
    /// and a protected blob, so the cost is a file write nobody notices, and
    /// the alternative is an Apply button on a panel where nothing else has
    /// one.
    /// </summary>
    private void MarkCredentialsChanged()
    {
        _ = SaveAsync("Saved sign-ins updated");
        OnPropertyChanged(nameof(IsManagingCredentials));
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when the document is empty, which needs its own screen.</summary>
    public bool IsEmpty => Root is null || Root.Children.Count == 0;

    /// <summary>What a delete would take with it, in words.</summary>
    public string DeleteWarning
    {
        get
        {
            if (SelectedNode is not { } node)
            {
                return string.Empty;
            }

            if (node.Model is ServerNode)
            {
                return $"Delete '{node.Name}'? This cannot be undone yet.";
            }

            int count = NodeOperations.CountServers(node.Model);

            return count switch
            {
                0 => $"Delete the empty group '{node.Name}'?",
                1 => $"Delete '{node.Name}' and the 1 connection inside it? This cannot be undone yet.",
                _ => string.Create(
                    CultureInfo.CurrentCulture,
                    $"Delete '{node.Name}' and the {count} connections inside it? This cannot be undone yet."),
            };
        }
    }

    /// <summary>Reads the document and builds the tree. Call once, at startup.</summary>
    public async Task InitialiseAsync()
    {
        try
        {
            LoadResult result = await _store.LoadAsync().ConfigureAwait(true);

            _document = result.Document;
            Notice = result.Notice;
            Status = result.IsClean ? $"Opened {FileName}" : $"Opened {FileName} with notes";
        }
        catch (ConnectionDocumentException ex)
        {
            // The store only throws when the file and every backup are
            // unreadable. Starting a blank document over the top would write
            // that loss to disk on the first save, so the tree stays empty and
            // the notice stays on screen.
            _document = new ConnectionDocument();
            Notice = ex.Message;
            Status = "Could not open the connection file";
        }

        // Whatever came back, including the empty document put up after a
        // failed load: the protection follows the document, and a stale key
        // from a previous one must not survive into it (M3-07).
        _protection.Open(_document);

        BuildTree();

        if (_protection.NeedsUnlocking)
        {
            // Opened on arrival rather than left for somebody to find. The
            // tree, the editor and every connection that does not use a saved
            // password work perfectly well locked, so this is not a barrier —
            // but a saved password failing with no explanation would be.
            OnPropertyChanged(nameof(IsDocumentLocked));
            ManageSecurity();
            Status = $"Opened {FileName} · locked";
        }
        else if (_protection.NamesAnUnknownStore)
        {
            // The same reasoning one step down (M3-04). This document keeps
            // its passwords somewhere this build has never heard of, so
            // everything works except saving a password — and that would fail
            // later, with a message about a store nobody has mentioned yet.
            ManageSecurity();
            Status = $"Opened {FileName} · no password store";
        }
    }

    [RelayCommand]
    private void NewConnection() =>
        GuardUnsaved(() =>
        {
            Editor = EditorViewModel.ForNewServer(TargetGroup(), CredentialChoices());
            IsDeleteRequested = false;
        });

    [RelayCommand]
    private void NewGroup() =>
        GuardUnsaved(() =>
        {
            Editor = EditorViewModel.ForNewGroup(TargetGroup(), CredentialChoices());
            IsDeleteRequested = false;
        });

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditSelected() =>
        GuardUnsaved(() =>
        {
            if (SelectedNode is { } node)
            {
                Editor = EditorViewModel.ForExisting(node.Model, CredentialChoices());
                IsDeleteRequested = false;
            }
        });

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DuplicateSelectedAsync()
    {
        if (SelectedNode is not { ParentNode: { } parentNode } node)
        {
            return;
        }

        ConnectionNode copy = NodeOperations.Duplicate(node.Model);
        copy.Name = NodeOperations.UniqueName((GroupNode)parentNode.Model, copy.Name);

        NodeViewModel added = parentNode.AddChild(copy);
        Select(added);
        OnPropertyChanged(nameof(IsEmpty));

        await SaveAsync($"Duplicated '{node.Name}'").ConfigureAwait(true);
    }

    /// <summary>
    /// Asks rather than deletes. The confirmation is an inline bar in the
    /// detail panel; a modal box would be invisible once a session is on
    /// screen, and would be the wrong shape for this anyway.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void RequestDelete()
    {
        OnPropertyChanged(nameof(DeleteWarning));
        IsDeleteRequested = true;
    }

    [RelayCommand]
    private void CancelDelete() => IsDeleteRequested = false;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task ConfirmDeleteAsync()
    {
        if (SelectedNode is not { ParentNode: { } parentNode } node)
        {
            return;
        }

        string name = node.Name;

        node.RemoveFromParent();
        IsDeleteRequested = false;

        Select(parentNode);
        OnPropertyChanged(nameof(IsEmpty));

        await SaveAsync($"Deleted '{name}'").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        ConnectionNode? node = editor.TryCommit();

        if (node is null)
        {
            return;
        }

        string what;

        if (editor.IsNew)
        {
            NodeViewModel parentNode = FindNode(node.Parent) ?? NodeFor(TargetGroup()) ?? Root!;
            NodeViewModel added = parentNode.AddChild(node);

            Select(added);
            what = $"Added '{node.Name}'";
        }
        else
        {
            NodeViewModel? existing = FindNode(node);
            existing?.Refresh();
            existing?.RefreshAncestorSummaries();

            Select(existing);
            what = $"Saved '{node.Name}'";
        }

        Editor = null;
        OnPropertyChanged(nameof(IsEmpty));
        RefreshDetail();

        await SaveAsync(what).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelEditor()
    {
        if (Editor is { IsDirty: true } dirty)
        {
            dirty.IsDiscardPromptVisible = true;
            return;
        }

        Editor = null;
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        Editor = null;

        Action? next = _afterDiscard;
        _afterDiscard = null;
        next?.Invoke();
    }

    [RelayCommand]
    private void KeepEditing()
    {
        _afterDiscard = null;

        if (Editor is { } editor)
        {
            editor.IsDiscardPromptVisible = false;
        }
    }

    [RelayCommand]
    private async Task ImportRdgAsync()
    {
        if (Editor is { IsDirty: true } dirty)
        {
            _afterDiscard = () => ImportRdgCommand.Execute(null);
            dirty.IsDiscardPromptVisible = true;
            return;
        }

        if (_chooseImportFile?.Invoke() is not { } path)
        {
            return;
        }

        Editor = null;

        await ImportAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Imports an RDCMan file into a group of its own, rather than merging it
    /// into the tree. Someone importing a colleague's file wants to see what
    /// arrived before it is mixed in with what they already had, and moving a
    /// group afterwards is one drag.
    /// </summary>
    public async Task ImportAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Root is not { } root)
        {
            return;
        }

        ImportResult result;

        try
        {
            // Off the interface thread: parsing is bounded but not instant,
            // and a large estate should not freeze the window.
            result = await Task.Run(() => RdgImporter.ImportFile(path)).ConfigureAwait(true);
        }
        catch (ImportException ex)
        {
            Notice = $"{Path.GetFileName(path)} could not be imported. {ex.Message}";
            Status = "Import failed";
            return;
        }

        GroupNode imported = result.Root;
        imported.Name = NodeOperations.UniqueName((GroupNode)root.Model, imported.Name);

        NodeViewModel added = root.AddChild(imported);
        Select(added);

        OnPropertyChanged(nameof(IsEmpty));
        RefreshDetail();

        Notice = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { result.Summary }.Concat(result.Warnings));

        await SaveAsync($"Imported {Path.GetFileName(path)}").ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the selected machine in a tab and connects it, or brings its tab
    /// forward if it is already open. Bound to Enter and to a double-click on
    /// the tree (M2-17).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnectSelected))]
    private async Task ConnectSelectedAsync()
    {
        if (SelectedNode?.Model is not ServerNode server)
        {
            return;
        }

        EffectiveSettings effective = SettingsResolver.Resolve(server);
        CredentialResolution sign = _credentials.Resolve(_document, effective.Values);

        SessionRequest request = SessionRequest.For(effective) with
        {
            Credentials = sign.Credentials,
        };

        IRemoteSession session = _workspace.Open(request);

        SessionTabViewModel tab =
            Tabs.FirstOrDefault(t => ReferenceEquals(t.Session, session)) ?? AddTab(session);

        ActiveTab = tab;

        if (!tab.CanConnect)
        {
            // Already live, or on its way. Bringing it forward is the whole of
            // what was being asked for.
            Status = $"{tab.Title} · {tab.StateLabel}";
            return;
        }

        tab.ForgetReconnect();

        // Ask before connecting rather than after failing (M3-05). With
        // network level authentication required there is no logon screen to
        // fall back on — the attempt simply fails — so a connection set to
        // prompt has to be asked first or not asked at all.
        if (NeedsAsking(tab, sign))
        {
            Status = $"{tab.Title} needs a sign-in";
            return;
        }

        if (sign.Notice is { } notice)
        {
            Status = notice;
        }

        await ConnectAsync(tab).ConfigureAwait(true);
    }

    /// <summary>
    /// Docks a panel and answers whether connecting should wait for it
    /// (M3-05).
    ///
    /// <para>
    /// The cache is the session's own <see cref="SessionRequest.Credentials"/>,
    /// put there by <c>UseCredentials</c> when a panel was answered. That is
    /// what makes this per session rather than per attempt: a reconnect after
    /// a dropped link reuses what was typed, and closing the tab forgets it
    /// along with everything else the session held. Nothing is cached across
    /// tabs, so two tabs on the same machine ask separately — which is
    /// tedious exactly once and wrong every time the alternative is taken.
    /// </para>
    /// </summary>
    private bool NeedsAsking(SessionTabViewModel tab, CredentialResolution sign)
    {
        if (!sign.NeedsPrompt || sign.PromptReason is not { } reason)
        {
            return false;
        }

        // Already answered for this session. An automatic reconnect (M4-08)
        // must not stop to ask at three in the morning, and M4-08 already
        // refuses to retry a refusal, so a stale cached password cannot be
        // fed to an account until it locks.
        if (!tab.Session.Request.Credentials.IsEmpty)
        {
            return false;
        }

        tab.Ask(new CredentialPrompt(
            tab.Endpoint,
            reason,
            sign.Credentials,
            _credentials.CanSavePasswords));

        return true;
    }

    /// <summary>Connects a tab that is not connected. Retry and reconnect are the same gesture.</summary>
    [RelayCommand]
    private async Task ReconnectAsync(SessionTabViewModel? tab)
    {
        if (tab is { CanConnect: true })
        {
            // By hand, so whatever the automatic sequence had got to is over
            // (M4-08): somebody has taken charge, and the next drop starts with
            // a full set of attempts rather than the remains of the last one.
            tab.ForgetReconnect();

            await ConnectAsync(tab).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Stops a countdown (M4-08). The tab stays exactly where it is, still
    /// saying why the session went, still offering a connect — cancelling the
    /// wait is not the same as being done with the machine.
    /// </summary>
    [RelayCommand]
    private void CancelReconnect(SessionTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        tab.CancelReconnect();
        Status = $"{tab.Title} · reconnecting cancelled";
    }

    [RelayCommand]
    private async Task DisconnectTabAsync(SessionTabViewModel? tab)
    {
        if (tab is not null)
        {
            await tab.DisconnectAsync().ConfigureAwait(true);
            Status = $"{tab.Title} · {tab.StateLabel}";
        }
    }

    /// <summary>Back to the tree and the detail panel. The permanent tab.</summary>
    [RelayCommand]
    private void ShowConnections() => ActiveTab = null;

    [RelayCommand]
    private void ActivateTab(SessionTabViewModel? tab)
    {
        if (tab is not null && Tabs.Contains(tab))
        {
            ActiveTab = tab;
        }
    }

    /// <summary>
    /// Turns smart sizing on or off for a tab, or for the one on screen when
    /// nothing is named (M5-09).
    ///
    /// A button beside the session rather than over it, and not a keyboard
    /// shortcut: WPF drawn over a hosted session is invisible (M4-03) and a
    /// live session takes the keyboard (M5-06, M5-07). mstsc puts the same
    /// toggle in its system menu.
    ///
    /// The choice belongs to the tab and is not written back to the document.
    /// It is a way of looking at a session, not a change to the connection.
    /// </summary>
    [RelayCommand]
    private void ToggleSmartSizing(SessionTabViewModel? tab)
    {
        SessionTabViewModel? target = tab ?? ActiveTab;

        if (target is null)
        {
            return;
        }

        target.SmartSizing = !target.SmartSizing;
        Status = $"{target.Title}: {target.SizingLabel}";
    }

    /// <summary>
    /// Closes a tab and ends its session. Which tab comes forward next is the
    /// workspace's decision, not this one's.
    /// </summary>
    [RelayCommand]
    private void CloseTab(SessionTabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
        {
            return;
        }

        string name = tab.Title;

        tab.ReconnectScheduled -= OnReconnectScheduled;
        tab.PropertyChanged -= OnTabPropertyChanged;

        _workspace.Close(tab.Session);
        Tabs.Remove(tab);
        tab.Dispose();

        OnPropertyChanged(nameof(HasTabs));
        ActiveTab = Tabs.FirstOrDefault(t => ReferenceEquals(t.Session, _workspace.Active));

        Status = $"Closed {name}";
    }

    /// <summary>
    /// Ends every session. Called when the window closes, because a session
    /// left running holds a socket and a decoder open until the process goes
    /// away. Safe to call twice.
    /// </summary>
    public void Dispose()
    {
        _reconnectTimer.Stop();
        _clipboardTimer.Stop();

        // A password left on the clipboard by a process that has gone will
        // never be cleared by anything (M3-09).
        _clipboard.ClearNow();

        foreach (SessionTabViewModel tab in Tabs)
        {
            tab.ReconnectScheduled -= OnReconnectScheduled;
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.Dispose();
        }

        Tabs.Clear();
        _workspace.Dispose();

        // The document key goes with the process that held it (M3-07).
        _protection.Dispose();

        ActiveTab = null;
        OnPropertyChanged(nameof(HasTabs));
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ExpandAll() => Root?.SetExpandedThroughout(true);

    [RelayCommand]
    private void CollapseAll()
    {
        Root?.SetExpandedThroughout(false);

        if (Root is { } root)
        {
            root.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeManager.Toggle();
        ThemeLabel = OppositeThemeLabel();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    private bool CanDelete => SelectedNode is { ParentNode: not null };

    private bool CanConnectSelected => SelectedNode?.Model is ServerNode;

    private static string OppositeThemeLabel() =>
        ThemeManager.Resolved is AppTheme.Dark ? "Light theme" : "Dark theme";

    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        IsDeleteRequested = false;

        OnPropertyChanged(nameof(HasSelection));
        EditSelectedCommand.NotifyCanExecuteChanged();
        DuplicateSelectedCommand.NotifyCanExecuteChanged();
        ConnectSelectedCommand.NotifyCanExecuteChanged();
        RequestDeleteCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();

        RefreshDetail();
    }

    partial void OnEditorChanged(EditorViewModel? value)
    {
        OnPropertyChanged(nameof(IsEditing));

        // The sign-in manager shares this column (M3-10), so leaving both set
        // would draw one over the other.
        if (value is not null)
        {
            CredentialManager = null;
        }
    }

    partial void OnActiveTabChanged(SessionTabViewModel? oldValue, SessionTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
            _workspace.Activate(newValue.Session);
        }

        OnPropertyChanged(nameof(IsSessionVisible));
        OnPropertyChanged(nameof(IsBrowsing));
    }

    private SessionTabViewModel AddTab(IRemoteSession session)
    {
        SessionTabViewModel tab = new(session);
        tab.ReconnectScheduled += OnReconnectScheduled;
        tab.CredentialsRequested += OnCredentialsRequested;
        tab.PropertyChanged += OnTabPropertyChanged;

        Tabs.Add(tab);
        OnPropertyChanged(nameof(HasTabs));

        return tab;
    }

    /// <summary>
    /// Keeps the status line honest about a session nobody was watching.
    ///
    /// Until sessions could end on their own without anybody asking (M4-08),
    /// every state the line reported was one somebody had just caused, and it
    /// was written where the causing happened. A session that drops at three in
    /// the morning has nobody to write it, and a line still reading "Connected"
    /// beside a countdown is worse than one reading nothing.
    /// </summary>
    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(SessionTabViewModel.StateLabel)
            || sender is not SessionTabViewModel tab
            || !ReferenceEquals(tab, ActiveTab))
        {
            return;
        }

        Status = $"{tab.Title} · {tab.StateLabel}";
    }

    // ── The clipboard (M3-09) ───────────────────────────────────────────

    /// <summary>
    /// Copies the account this session is signed in as.
    ///
    /// <para>
    /// A button beside the session rather than a keyboard shortcut, for the
    /// same reason as the sizing toggle: a live session takes the keyboard
    /// until M5-06 and M5-07 say otherwise, so Ctrl+C over a session belongs
    /// to the far end.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void CopyUserName()
    {
        if (ActiveTab?.Session.Request.Credentials is not { UserName.Length: > 0 } sign)
        {
            return;
        }

        _clipboard.CopyUserName(sign.Display);
        AfterClipboard();
    }

    /// <summary>
    /// Copies the password this session was given, for thirty seconds.
    ///
    /// <para>
    /// Only from a session, and deliberately not from the credential manager.
    /// Patchbay has already sent this password to this server, so putting it
    /// on the clipboard to be pasted into that same server's own logon screen
    /// reveals nothing it has not already done — which is the case for the
    /// feature, since the reason to want it is that credential injection did
    /// not reach the far end. A manager that hands back saved passwords is a
    /// different claim, and M3-10 decided against it.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void CopyPassword()
    {
        if (ActiveTab?.Session.Request.Credentials is not { HasPassword: true } sign)
        {
            return;
        }

        _clipboard.CopyPassword(sign.Password);
        AfterClipboard();
    }

    /// <summary>
    /// Says what happened and starts the clock if there is now something to
    /// take back off the clipboard.
    /// </summary>
    private void AfterClipboard()
    {
        Status = _clipboard.Notice ?? Status;

        if (!_clipboard.IsCountingDown)
        {
            _clipboardTimer.Stop();
            return;
        }

        _sinceClipboardTick.Restart();

        if (!_clipboardTimer.IsEnabled)
        {
            _clipboardTimer.Start();
        }
    }

    /// <summary>
    /// One second of the countdown. The remaining time is shown rather than
    /// kept quiet: a password that is about to be taken off the clipboard is
    /// something somebody needs to know before they walk away from it.
    /// </summary>
    private void OnClipboardTick(object? sender, EventArgs e)
    {
        TimeSpan elapsed = _sinceClipboardTick.Elapsed;
        _sinceClipboardTick.Restart();

        bool running = _clipboard.Tick(elapsed);

        Status = _clipboard.Notice ?? Status;

        if (!running)
        {
            _clipboardTimer.Stop();
        }
    }

    /// <summary>
    /// A tab has a countdown running, so the clock has to be going (M4-08).
    /// Starting an already-started <see cref="DispatcherTimer"/> restarts its
    /// interval, which would postpone every other tab's tick, so it is only
    /// started when it is not already running. Each tab measures its own
    /// elapsed time, so joining a clock that is already going costs nothing.
    /// </summary>
    private void OnReconnectScheduled(object? sender, EventArgs e)
    {
        if (!_reconnectTimer.IsEnabled)
        {
            _reconnectTimer.Start();
        }
    }

    /// <summary>
    /// One second of every countdown (M4-08). How much time has actually passed
    /// is each tab's own business — see <see cref="SessionTabViewModel.Tick"/>.
    /// </summary>
    private async void OnReconnectTick(object? sender, EventArgs e)
    {
        // Copied, because connecting a tab can end with it being closed.
        SessionTabViewModel[] due = [.. Tabs.Where(tab => tab.Tick())];

        if (!Tabs.Any(tab => tab.IsReconnecting))
        {
            _reconnectTimer.Stop();
        }

        foreach (SessionTabViewModel tab in due)
        {
            if (Tabs.Contains(tab) && tab.CanConnect)
            {
                // Not ForgetReconnect: this attempt is the sequence's, and
                // clearing the count here is how an attempt limit becomes
                // unreachable.
                await ConnectAsync(tab).ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Docks a credential panel on a tab whose sign-in was refused (M3-06).
    ///
    /// The account is carried over and the password never is: pre-filling the
    /// one that was just turned down invites somebody to press Connect again
    /// without reading. Whether saving is offered comes from the vault rather
    /// than from a guess, so the box does not appear on an account that cannot
    /// keep a password (M3-02).
    /// </summary>
    private void OnCredentialsRequested(object? sender, EventArgs e)
    {
        if (sender is not SessionTabViewModel tab)
        {
            return;
        }

        tab.Ask(new CredentialPrompt(
            tab.Endpoint,
            CredentialPromptReason.Refused,
            tab.Session.Request.Credentials,
            _credentials.CanSavePasswords));
    }

    /// <summary>
    /// Answers the docked panel: takes the sign-in, optionally keeps it, and
    /// reconnects the same tab (M3-06, M4-10).
    ///
    /// The session goes and the tab does not, which is the whole shape of the
    /// item. The RDP control reads credentials once as the connection is made,
    /// so there is no way to hand them to the session already up; what there
    /// is, is a reconnect into the tab somebody is already looking at, with
    /// its history and its place in the strip intact.
    /// </summary>
    [RelayCommand]
    private async Task SubmitCredentialsAsync(SessionTabViewModel? tab)
    {
        if (tab?.Prompt is not { CanSubmit: true } prompt)
        {
            return;
        }

        SessionCredentials answer = prompt.Prompt.ToCredentials();
        bool save = prompt.SavePassword;

        // Off the panel before anything can fail, so a refused save does not
        // leave the typed password sitting in a control on screen.
        tab.StopAsking();

        if (save)
        {
            SaveToProfile(tab, answer);
        }

        tab.Session.UseCredentials(answer);

        // Down before up, and safe on a session that was never up: a panel
        // docked before the first attempt (M3-05) leaves nothing to
        // disconnect, while one docked over a refused sign-in (M4-10) leaves a
        // session that is still connected and that ConnectAsync would refuse.
        await tab.Session.DisconnectAsync().ConfigureAwait(true);
        await ConnectAsync(tab).ConfigureAwait(true);
    }

    /// <summary>
    /// Dismisses the panel (M3-05).
    ///
    /// On one raised before connecting this connects anyway, with nothing, and
    /// the server shows its own logon screen. That is the way past, and
    /// without it a connection set to ask every time has no route to the
    /// screen it would have shown before this item existed — pressing
    /// Connect again would only put the panel back.
    ///
    /// On one raised over a refusal there is nowhere to go past to, so it
    /// simply goes away and leaves the session as it was.
    /// </summary>
    [RelayCommand]
    private async Task DismissCredentialsAsync(SessionTabViewModel? tab)
    {
        if (tab?.Prompt is not { } prompt)
        {
            return;
        }

        bool connectAnyway = prompt.Prompt.IsBeforeConnecting;

        tab.StopAsking();

        if (connectAnyway && tab.CanConnect)
        {
            await ConnectAsync(tab).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Keeps a password against the profile the connection names, when it
    /// names one.
    ///
    /// A connection set to prompt each time has nowhere to put it, and
    /// inventing a profile on its behalf would add a saved sign-in nobody
    /// asked for to a document somebody else may open. Creating profiles is
    /// M3-10; this only fills one in.
    /// </summary>
    private void SaveToProfile(SessionTabViewModel tab, SessionCredentials answer)
    {
        if (_document.FindById(tab.Session.Request.NodeId) is not { } node)
        {
            return;
        }

        ConnectionSettings resolved = SettingsResolver.Resolve(node).Values;

        if (resolved.CredentialProfileId is not { } id ||
            _document.FindCredential(id) is not { } profile)
        {
            Status = "There is no saved sign-in on this connection to keep that password in.";
            return;
        }

        try
        {
            _credentials.SavePassword(profile, answer.Password);
            profile.UserName = answer.UserName;
            profile.Domain = answer.Domain;
        }
        catch (SecretProtectionException ex)
        {
            // Said out loud rather than swallowed. The session still connects
            // with what was typed; what did not happen is the keeping.
            Status = $"The password could not be saved: {ex.Message}";
            return;
        }

        _ = SaveAsync($"Saved the password for {profile.Label}");
    }

    /// <summary>
    /// The saved sign-ins an editor can pick from (M3-10), newest list every
    /// time rather than a cached one: profiles are added and deleted from the
    /// manager while an editor may be open, and a picker offering a profile
    /// that has gone is worse than one that is a moment out of date.
    /// </summary>
    private IReadOnlyList<ChoiceOption> CredentialChoices() =>
        [.. _document.Credentials.Select(c => new ChoiceOption(c.Id, c.Label))];

    private async Task ConnectAsync(SessionTabViewModel tab)
    {
        Status = $"Connecting to {tab.Endpoint}";

        await tab.ConnectAsync().ConfigureAwait(true);

        Status = $"{tab.Title} · {tab.StateLabel}";
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void BuildTree()
    {
        Roots.Clear();

        NodeViewModel root = new(_document.Root, null);
        Roots.Add(root);

        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(IsEmpty));

        Select(root);
    }

    private void ApplySearch()
    {
        if (Root is not { } root)
        {
            return;
        }

        root.ApplyFilter(SearchText);

        // The root row itself is always shown; whether anything useful is
        // under it is the question the empty state answers.
        root.IsVisible = true;
        HasSearchResults = !IsSearching || root.Children.Any(c => c.IsVisible);
    }

    /// <summary>The group a new node should go into: the selection, or its parent.</summary>
    private GroupNode TargetGroup() =>
        SelectedNode?.Model switch
        {
            GroupNode group => group,
            ServerNode server when server.Parent is { } parent => parent,
            _ => _document.Root,
        };

    private NodeViewModel? NodeFor(ConnectionNode model) => FindNode(model);

    private NodeViewModel? FindNode(ConnectionNode? model) =>
        model is null
            ? null
            : Root?.DescendantsAndSelf().FirstOrDefault(n => ReferenceEquals(n.Model, model));

    private void Select(NodeViewModel? node)
    {
        if (node is null)
        {
            SelectedNode = null;
            return;
        }

        node.ExpandAncestors();
        node.IsSelected = true;

        // The tree raises its own selection change when IsSelected lands, but
        // only for rows it has realised. Setting it here as well keeps the
        // detail panel right for a row that is scrolled out of view.
        SelectedNode = node;
    }

    private void RefreshDetail() =>
        DetailSections = SelectedNode is { } node ? DetailBuilder.Build(node.Model) : [];

    /// <summary>
    /// Runs an action, unless there are unsaved edits — in which case it is
    /// held until the discard prompt is answered.
    /// </summary>
    private void GuardUnsaved(Action action)
    {
        if (Editor is { IsDirty: true } dirty)
        {
            _afterDiscard = action;
            dirty.IsDiscardPromptVisible = true;
            return;
        }

        action();
    }

    private async Task SaveAsync(string what)
    {
        try
        {
            await _store.SaveAsync(_document).ConfigureAwait(true);
            Status = $"{what} · saved to {FileName}";
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ConnectionDocumentException)
        {
            Notice = $"{what}, but it could not be written to disk: {ex.Message}";
            Status = "Not saved";
        }
    }
}
