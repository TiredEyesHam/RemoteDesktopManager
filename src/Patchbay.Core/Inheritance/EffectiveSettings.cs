using Patchbay.Core.Model;

namespace Patchbay.Core.Inheritance;

/// <summary>
/// A node's settings after inheritance has been applied: every property has a
/// value, and every property knows where that value came from.
///
/// The second half is the part that matters for the interface. Showing the
/// effective gateway is easy; showing "inherited from Production" next to it,
/// so someone can tell at a glance what they are about to override, is the
/// reason this type carries origins rather than just values.
/// </summary>
public sealed class EffectiveSettings
{
    private readonly IReadOnlyDictionary<string, ConnectionNode> _origins;

    internal EffectiveSettings(
        ConnectionNode node,
        ConnectionSettings values,
        IReadOnlyDictionary<string, ConnectionNode> origins)
    {
        Node = node;
        Values = values;
        _origins = origins;
    }

    /// <summary>The node these settings were resolved for.</summary>
    public ConnectionNode Node { get; }

    /// <summary>Fully resolved values. No property is null.</summary>
    public ConnectionSettings Values { get; }

    /// <summary>
    /// The node that supplied <paramref name="propertyName"/>, or null when it
    /// fell through to a built-in default. Pass a name from
    /// <c>nameof(ConnectionSettings.X)</c>.
    /// </summary>
    public ConnectionNode? SourceOf(string propertyName) =>
        _origins.TryGetValue(propertyName, out ConnectionNode? node) ? node : null;

    /// <summary>Whether a property is set here, inherited, or defaulted.</summary>
    public SettingOrigin OriginOf(string propertyName)
    {
        ConnectionNode? source = SourceOf(propertyName);

        if (source is null)
        {
            return SettingOrigin.Default;
        }

        return ReferenceEquals(source, Node) ? SettingOrigin.DefinedHere : SettingOrigin.Inherited;
    }

    /// <summary>
    /// Label for the inheritance chip, e.g. "Production" for an inherited
    /// value, "Override" for a local one, "Default" when nothing set it.
    /// </summary>
    public string DescribeOrigin(string propertyName) => OriginOf(propertyName) switch
    {
        SettingOrigin.DefinedHere => "Override",
        SettingOrigin.Inherited => SourceOf(propertyName)!.Name,
        _ => "Default",
    };
}
