using System.Collections.ObjectModel;
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

    private readonly IConnectionStore _store;
    private readonly Func<string?>? _chooseImportFile;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly SessionWorkspace _workspace;

    private ConnectionDocument _document = new();
    private Action? _afterDiscard;

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private EditorViewModel? _editor;

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
        IRemoteSessionHost? sessionHost = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _chooseImportFile = chooseImportFile;

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

        BuildTree();
    }

    [RelayCommand]
    private void NewConnection() =>
        GuardUnsaved(() =>
        {
            Editor = EditorViewModel.ForNewServer(TargetGroup());
            IsDeleteRequested = false;
        });

    [RelayCommand]
    private void NewGroup() =>
        GuardUnsaved(() =>
        {
            Editor = EditorViewModel.ForNewGroup(TargetGroup());
            IsDeleteRequested = false;
        });

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditSelected() =>
        GuardUnsaved(() =>
        {
            if (SelectedNode is { } node)
            {
                Editor = EditorViewModel.ForExisting(node.Model);
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

        SessionRequest request = SessionRequest.For(SettingsResolver.Resolve(server));
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

        await ConnectAsync(tab).ConfigureAwait(true);
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
    /// It has to be a button beside the session rather than one over it, and
    /// it cannot be a keyboard shortcut either: WPF drawn over a hosted
    /// session is not visible (M4-03), and a live session takes the keyboard
    /// until it is told not to (M5-06, M5-07). mstsc puts the same toggle in
    /// its system menu for the same reason.
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

        foreach (SessionTabViewModel tab in Tabs)
        {
            tab.ReconnectScheduled -= OnReconnectScheduled;
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.Dispose();
        }

        Tabs.Clear();
        _workspace.Dispose();

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

    partial void OnEditorChanged(EditorViewModel? value) => OnPropertyChanged(nameof(IsEditing));

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
