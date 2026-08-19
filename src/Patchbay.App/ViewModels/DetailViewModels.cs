using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;

namespace Patchbay.App.ViewModels;

/// <summary>
/// One resolved setting as it appears in the read view: the value, and where
/// it came from.
///
/// The origin is the part worth having. Any tool can show that a connection
/// uses port 3389; showing that the port came from the Production group three
/// levels up is what tells someone whether changing it here is safe.
/// </summary>
public sealed record DetailRow(string Label, string Value, string Origin, SettingOrigin Kind)
{
    public bool IsOverride => Kind is SettingOrigin.DefinedHere;

    public bool IsInherited => Kind is SettingOrigin.Inherited;

    public bool IsDefault => Kind is SettingOrigin.Default;
}

/// <summary>A headed run of settings in the read view.</summary>
public sealed record DetailSection(string Title, IReadOnlyList<DetailRow> Rows);

/// <summary>Turns a node into the read view's contents.</summary>
public static class DetailBuilder
{
    public static IReadOnlyList<DetailSection> Build(ConnectionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        EffectiveSettings effective = SettingsResolver.Resolve(node);

        return
        [
            .. SettingCatalogue.Sections.Select(section => new DetailSection(
                section,
                [
                    .. SettingCatalogue.All
                        .Where(d => string.Equals(d.Section, section, StringComparison.Ordinal))
                        .Where(d => d.Kind is not SettingKind.Hidden
                            || SettingCatalogue.Read(effective.Values, d.PropertyName) is not null)
                        .Select(d => new DetailRow(
                            d.Label,
                            SettingDisplay.Describe(
                                SettingCatalogue.Read(effective.Values, d.PropertyName), d),
                            effective.DescribeOrigin(d.PropertyName),
                            effective.OriginOf(d.PropertyName)))
                ]))
        ];
    }
}
