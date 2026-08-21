using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;
using Patchbay.Core.Validation;

namespace Patchbay.App.ViewModels;

/// <summary>A headed run of editable settings.</summary>
public sealed record SettingSectionViewModel(string Title, IReadOnlyList<SettingFieldViewModel> Fields);

/// <summary>
/// The docked editor, for both adding and changing.
///
/// It edits a draft rather than the live node: a clone of the settings plus
/// loose copies of the name and address. Nothing reaches the document until
/// Save succeeds, so Cancel is genuinely free, and a validation failure leaves
/// the tree exactly as it was rather than half-updated.
///
/// It is a docked panel and not a dialog on purpose. Once a session is on
/// screen (M4) it is an ActiveX window painting over everything WPF draws,
/// and anything floating above it would be invisible.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly ConnectionSettings _draft;
    private readonly ConnectionNode? _existing;
    private readonly GroupNode? _parent;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _hostName = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _hostNameError;

    [ObservableProperty]
    private string? _generalError;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Set when a cancel is refused because there are unsaved changes. The
    /// answer is an inline bar in the panel, not a modal box.
    /// </summary>
    [ObservableProperty]
    private bool _isDiscardPromptVisible;

    private EditorViewModel(
        ConnectionNode? existing,
        GroupNode? parent,
        bool isServer,
        string title,
        IReadOnlyList<ChoiceOption>? credentials = null)
    {
        _existing = existing;
        _parent = parent;

        IsServer = isServer;
        Title = title;

        _draft = existing?.Settings.Clone() ?? new ConnectionSettings();

        if (existing is not null)
        {
            _name = existing.Name;
            _notes = existing.Notes ?? string.Empty;

            if (existing is ServerNode server)
            {
                _hostName = server.HostName;
                _tags = string.Join(", ", server.Tags);
            }
        }

        // What this node would get if it overrode nothing. For the root there
        // is no ancestry, so the built-in defaults are the whole story.
        EffectiveSettings? inherited = parent is null ? null : SettingsResolver.Resolve(parent);

        List<SettingFieldViewModel> fields = [];

        foreach (SettingDescriptor descriptor in SettingCatalogue.Editable)
        {
            SettingFieldViewModel field = new(
                descriptor,
                _draft,
                inherited,
                descriptor.Kind is SettingKind.Credential ? credentials : null);
            field.PropertyChanged += OnFieldChanged;
            fields.Add(field);
        }

        Fields = fields;

        Sections =
        [
            .. SettingCatalogue.Sections
                .Select(section => new SettingSectionViewModel(
                    section,
                    [.. fields.Where(f => string.Equals(f.Descriptor.Section, section, StringComparison.Ordinal))]))
                .Where(s => s.Fields.Count > 0)
        ];
    }

    /// <summary>Editor for a connection that does not exist yet.</summary>
    public static EditorViewModel ForNewServer(
        GroupNode parent,
        IReadOnlyList<ChoiceOption>? credentials = null) =>
        new(null, parent, isServer: true, "New connection", credentials);

    /// <summary>Editor for a group that does not exist yet.</summary>
    public static EditorViewModel ForNewGroup(
        GroupNode parent,
        IReadOnlyList<ChoiceOption>? credentials = null) =>
        new(null, parent, isServer: false, "New group", credentials);

    /// <summary>Editor for something already in the document.</summary>
    public static EditorViewModel ForExisting(
        ConnectionNode node,
        IReadOnlyList<ChoiceOption>? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new EditorViewModel(
            node,
            node.Parent,
            node is ServerNode,
            node is ServerNode ? "Edit connection" : "Edit group",
            credentials);
    }

    public string Title { get; }

    public bool IsServer { get; }

    public bool IsNew => _existing is null;

    /// <summary>The root has no address, no parent, and cannot be deleted.</summary>
    public bool IsRoot => _existing is not null && _existing.Parent is null;

    public IReadOnlyList<SettingFieldViewModel> Fields { get; }

    public IReadOnlyList<SettingSectionViewModel> Sections { get; }

    /// <summary>Where a new node will land, in words.</summary>
    public string ParentPath => _parent?.DisplayPath ?? string.Empty;

    /// <summary>
    /// Validates the draft and, if it holds up, writes it into the document.
    /// Returns the node that was created or changed, or null when something is
    /// wrong — in which case the error properties say what.
    /// </summary>
    public ConnectionNode? TryCommit()
    {
        NameError = null;
        HostNameError = null;
        GeneralError = null;

        SettingFieldViewModel? brokenField = Fields.FirstOrDefault(f => f.Error is not null);

        if (brokenField is not null)
        {
            GeneralError = $"{brokenField.Label}: {brokenField.Error}";
            return null;
        }

        IReadOnlyList<ValidationIssue> issues = IsServer
            ? NodeValidator.ValidateServer(Name, HostName, _draft.Port, _parent, _existing)
            : NodeValidator.ValidateGroup(Name, _parent, _existing);

        foreach (ValidationIssue issue in issues)
        {
            switch (issue.Field)
            {
                case NodeValidator.NameField:
                    NameError = issue.Message;
                    break;

                case NodeValidator.HostNameField:
                    HostNameError = issue.Message;
                    break;

                default:
                    GeneralError = issue.Message;
                    break;
            }
        }

        if (issues.Count > 0)
        {
            return null;
        }

        ConnectionNode node = _existing ?? (IsServer ? new ServerNode() : new GroupNode());

        node.Name = Name.Trim();
        node.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        node.Settings = _draft.Clone();

        if (node is ServerNode target)
        {
            target.HostName = HostName.Trim();

            target.Tags.Clear();

            foreach (string tag in SplitTags(Tags))
            {
                target.Tags.Add(tag);
            }
        }

        IsDirty = false;
        return node;
    }

    private static IEnumerable<string> SplitTags(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnHostNameChanged(string value) => MarkDirty();

    partial void OnNotesChanged(string value) => MarkDirty();

    partial void OnTagsChanged(string value) => MarkDirty();

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Error is set by the field itself while validating; it is a
        // consequence of a change, not one.
        if (!string.Equals(e.PropertyName, nameof(SettingFieldViewModel.Error), StringComparison.Ordinal))
        {
            MarkDirty();
        }
    }

    /// <summary>
    /// No guard against firing during construction is needed: the name and
    /// address are assigned to their backing fields directly, and the setting
    /// fields are subscribed to only once they are fully built.
    /// </summary>
    private void MarkDirty()
    {
        IsDirty = true;
        IsDiscardPromptVisible = false;
    }
}
