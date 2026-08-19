using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Patchbay.Core.Model;
using Patchbay.Core.Search;

namespace Patchbay.App.ViewModels;

/// <summary>
/// One row in the tree. Wraps a <see cref="ConnectionNode"/> and adds the
/// things that are about being on screen rather than about the connection:
/// whether the row is open, selected, or currently filtered out.
///
/// The view models mirror the model tree rather than binding to it directly,
/// because the model's <c>Children</c> is a plain list with no change
/// notification — and giving it one would push a presentation concern into a
/// type that also has to serialise cleanly.
/// </summary>
public sealed partial class NodeViewModel : ObservableObject
{
    /// <summary>Pixels of indent per level of depth.</summary>
    private const double IndentStep = 15;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Expansion as it stood before a search started, so clearing the box puts
    /// the tree back the way it was rather than leaving everything the search
    /// happened to open still open.
    /// </summary>
    private bool? _expandedBeforeSearch;

    public NodeViewModel(ConnectionNode model, NodeViewModel? parentNode)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
        ParentNode = parentNode;

        if (model is GroupNode group)
        {
            foreach (ConnectionNode child in group.Children)
            {
                Children.Add(new NodeViewModel(child, this));
            }
        }

        // The root starts open; a collapsed root is an empty window.
        IsExpanded = parentNode is null;
    }

    public ConnectionNode Model { get; }

    public NodeViewModel? ParentNode { get; }

    public ObservableCollection<NodeViewModel> Children { get; } = [];

    public bool IsGroup => Model is GroupNode;

    public bool IsServer => Model is ServerNode;

    public bool IsRoot => ParentNode is null;

    public bool HasChildren => Children.Count > 0;

    public Thickness Indent => new(Model.Depth * IndentStep, 0, 0, 0);

    public string Name => Model.Name;

    public string? HostName => (Model as ServerNode)?.HostName;

    public string? Notes => Model.Notes;

    /// <summary>
    /// Surfaced here rather than bound through to the model, because the model
    /// has no change notification: after a rename the trail would still read
    /// the old name until something else happened to redraw it.
    /// </summary>
    public string DisplayPath => Model.DisplayPath;

    public IReadOnlyList<string> Tags =>
        Model is ServerNode server ? [.. server.Tags] : [];

    /// <summary>The quiet second line: an address, or how much a group holds.</summary>
    public string Summary
    {
        get
        {
            if (Model is ServerNode server)
            {
                return server.HostName;
            }

            int count = ((GroupNode)Model).DescendantServers().Count();

            return count switch
            {
                0 => "Empty",
                1 => "1 connection",
                _ => string.Create(CultureInfo.CurrentCulture, $"{count} connections"),
            };
        }
    }

    /// <summary>Every node in this subtree, this one first.</summary>
    public IEnumerable<NodeViewModel> DescendantsAndSelf()
    {
        yield return this;

        foreach (NodeViewModel child in Children)
        {
            foreach (NodeViewModel nested in child.DescendantsAndSelf())
            {
                yield return nested;
            }
        }
    }

    /// <summary>Opens every group above this row so it can actually be seen.</summary>
    public void ExpandAncestors()
    {
        for (NodeViewModel? node = ParentNode; node is not null; node = node.ParentNode)
        {
            node.IsExpanded = true;
        }
    }

    public void SetExpandedThroughout(bool expanded)
    {
        foreach (NodeViewModel node in DescendantsAndSelf())
        {
            if (node.HasChildren)
            {
                node.IsExpanded = expanded;
            }
        }
    }

    /// <summary>
    /// Applies the search box to this subtree and reports whether anything in
    /// it survived. Groups holding a match are opened so the match is visible
    /// without any clicking, and their previous state is remembered so that
    /// clearing the box restores it.
    /// </summary>
    public bool ApplyFilter(string? query)
    {
        bool searching = !string.IsNullOrWhiteSpace(query);
        bool selfMatches = NodeFilter.MatchesSelf(Model, query);

        // A matching group keeps everything beneath it, matching or not: it is
        // the folder someone searched for, and emptying it would be a strange
        // way to answer that.
        string? childQuery = selfMatches ? null : query;

        bool anyChildVisible = false;

        foreach (NodeViewModel child in Children)
        {
            anyChildVisible |= child.ApplyFilter(childQuery);
        }

        IsVisible = selfMatches || anyChildVisible;

        if (searching)
        {
            _expandedBeforeSearch ??= IsExpanded;

            if (anyChildVisible)
            {
                IsExpanded = true;
            }
        }
        else if (_expandedBeforeSearch is bool previous)
        {
            IsExpanded = previous;
            _expandedBeforeSearch = null;
        }

        return IsVisible;
    }

    /// <summary>Re-reads everything this row shows from the model.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HostName));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(DisplayPath));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(Indent));
        OnPropertyChanged(nameof(HasChildren));
    }

    /// <summary>Adds a child row, keeping the model and the view in step.</summary>
    public NodeViewModel AddChild(ConnectionNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        ((GroupNode)Model).Add(child);

        NodeViewModel viewModel = new(child, this);
        Children.Add(viewModel);

        IsExpanded = true;
        Refresh();
        RefreshAncestorSummaries();

        return viewModel;
    }

    /// <summary>Removes this row and its node from the document.</summary>
    public void RemoveFromParent()
    {
        if (ParentNode is null)
        {
            throw new InvalidOperationException("The root cannot be removed.");
        }

        ((GroupNode)ParentNode.Model).Remove(Model);
        ParentNode.Children.Remove(this);
        ParentNode.Refresh();
        ParentNode.RefreshAncestorSummaries();
    }

    /// <summary>
    /// A group's summary counts what is below it, so adding or removing a
    /// server changes the wording on every group above it too.
    /// </summary>
    public void RefreshAncestorSummaries()
    {
        for (NodeViewModel? node = ParentNode; node is not null; node = node.ParentNode)
        {
            node.Refresh();
        }
    }

    public override string ToString() => Name;
}
