using System.Reflection;
using Patchbay.Core.Model;

namespace Patchbay.Core.Inheritance;

/// <summary>
/// Walks a node's ancestry and produces its effective settings.
///
/// The walk is per-property, not per-node: for each setting independently, the
/// nearest ancestor that supplies a value wins. So a server can take its
/// credentials from its immediate group and its gateway from three levels up,
/// which is exactly how people expect it to behave and is the thing every
/// naive "closest whole settings object wins" implementation gets wrong.
///
/// Property discovery is reflective on purpose. The settings surface is large
/// and still growing (M1-03), and a hand-written merge is one forgotten line
/// away from a setting that silently never inherits. Resolution runs on
/// selection change, not in a loop, so the cost does not matter; the property
/// list is cached regardless.
/// </summary>
public static class SettingsResolver
{
    private static readonly PropertyInfo[] SettingProperties =
    [
        .. typeof(ConnectionSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
    ];

    /// <summary>Names of every property that takes part in inheritance.</summary>
    public static IReadOnlyList<string> InheritableProperties { get; } =
        [.. SettingProperties.Select(p => p.Name)];

    /// <summary>
    /// Resolves <paramref name="node"/> against its ancestry, falling back to
    /// <paramref name="defaults"/> for anything nobody sets.
    /// </summary>
    /// <param name="node">The node to resolve.</param>
    /// <param name="defaults">
    /// Final fallback. Defaults to <see cref="ConnectionSettings.Defaults"/>.
    /// Values taken from here report <see cref="SettingOrigin.Default"/>.
    /// </param>
    public static EffectiveSettings Resolve(ConnectionNode node, ConnectionSettings? defaults = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        defaults ??= ConnectionSettings.Defaults;

        ConnectionSettings values = new();
        Dictionary<string, ConnectionNode> origins = new(StringComparer.Ordinal);

        // Materialised once rather than re-walked for each of ~17 properties.
        ConnectionNode[] chain = [.. node.AncestorsAndSelf()];

        foreach (PropertyInfo property in SettingProperties)
        {
            object? resolved = null;
            ConnectionNode? source = null;

            foreach (ConnectionNode ancestor in chain)
            {
                object? candidate = property.GetValue(ancestor.Settings);

                if (candidate is not null)
                {
                    resolved = candidate;
                    source = ancestor;
                    break;
                }
            }

            if (source is not null)
            {
                origins[property.Name] = source;
            }
            else
            {
                // Nobody set it. Fall back to the default, and leave the
                // property out of origins so OriginOf reports Default.
                resolved = property.GetValue(defaults);
            }

            property.SetValue(values, resolved);
        }

        return new EffectiveSettings(node, values, origins);
    }

    /// <summary>
    /// Clears a property on <paramref name="node"/> so it inherits again.
    /// The inspector's inherit/override toggle (M2-19) calls this.
    /// </summary>
    /// <exception cref="ArgumentException">No such setting.</exception>
    public static void ClearOverride(ConnectionNode node, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(node);

        PropertyInfo property = FindProperty(propertyName);
        property.SetValue(node.Settings, null);
    }

    /// <summary>
    /// Whether <paramref name="node"/> sets a property itself, regardless of
    /// what it would inherit.
    /// </summary>
    public static bool HasOverride(ConnectionNode node, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(node);

        return FindProperty(propertyName).GetValue(node.Settings) is not null;
    }

    private static PropertyInfo FindProperty(string propertyName)
    {
        PropertyInfo? property = Array.Find(
            SettingProperties,
            p => string.Equals(p.Name, propertyName, StringComparison.Ordinal));

        return property ?? throw new ArgumentException(
            $"'{propertyName}' is not a setting on {nameof(ConnectionSettings)}.",
            nameof(propertyName));
    }
}
