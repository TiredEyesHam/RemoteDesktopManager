using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

public class SettingsResolverTests
{
    /// <summary>
    /// Root "Connections" → "Production" → "Web" → WEB-PRD-01.
    /// Three levels of group, so tests can tell "nearest ancestor" apart from
    /// "any ancestor" and from "the root".
    /// </summary>
    private static (ConnectionDocument Doc, GroupNode Prod, GroupNode Web, ServerNode Server) BuildTree()
    {
        ConnectionDocument doc = new();

        GroupNode prod = new() { Name = "Production" };
        GroupNode web = new() { Name = "Web" };
        ServerNode server = new() { Name = "WEB-PRD-01", HostName = "10.20.4.11" };

        doc.Root.Add(prod);
        prod.Add(web);
        web.Add(server);

        return (doc, prod, web, server);
    }

    [Fact]
    public void Value_set_on_the_node_wins_over_every_ancestor()
    {
        (_, GroupNode prod, _, ServerNode server) = BuildTree();
        prod.Settings.UserName = @"CORP\svc_rdadmin";
        server.Settings.UserName = @"CORP\sql_admin";

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.Equal(@"CORP\sql_admin", effective.Values.UserName);
        Assert.Equal(SettingOrigin.DefinedHere, effective.OriginOf(nameof(ConnectionSettings.UserName)));
        Assert.Equal("Override", effective.DescribeOrigin(nameof(ConnectionSettings.UserName)));
    }

    [Fact]
    public void Unset_value_comes_from_the_nearest_ancestor_that_sets_it()
    {
        (_, GroupNode prod, GroupNode web, ServerNode server) = BuildTree();
        prod.Settings.GatewayHostName = "rdg.corp.local";
        web.Settings.GatewayHostName = "rdg.web.corp.local";

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.Equal("rdg.web.corp.local", effective.Values.GatewayHostName);
        Assert.Same(web, effective.SourceOf(nameof(ConnectionSettings.GatewayHostName)));
        Assert.Equal("Web", effective.DescribeOrigin(nameof(ConnectionSettings.GatewayHostName)));
    }

    [Fact]
    public void Inheritance_reaches_past_ancestors_that_do_not_set_the_value()
    {
        (_, GroupNode prod, _, ServerNode server) = BuildTree();
        prod.Settings.GatewayHostName = "rdg.corp.local";

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.Equal("rdg.corp.local", effective.Values.GatewayHostName);
        Assert.Same(prod, effective.SourceOf(nameof(ConnectionSettings.GatewayHostName)));
        Assert.Equal(SettingOrigin.Inherited, effective.OriginOf(nameof(ConnectionSettings.GatewayHostName)));
    }

    /// <summary>
    /// The behaviour a "nearest whole settings object wins" implementation gets
    /// wrong: each property resolves on its own, so one value can come from the
    /// immediate parent while another comes from two levels further up.
    /// </summary>
    [Fact]
    public void Each_property_resolves_independently_of_the_others()
    {
        (_, GroupNode prod, GroupNode web, ServerNode server) = BuildTree();
        prod.Settings.GatewayHostName = "rdg.corp.local";
        web.Settings.UserName = @"CORP\web_admin";
        server.Settings.DesktopWidth = 2560;

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.Same(prod, effective.SourceOf(nameof(ConnectionSettings.GatewayHostName)));
        Assert.Same(web, effective.SourceOf(nameof(ConnectionSettings.UserName)));
        Assert.Same(server, effective.SourceOf(nameof(ConnectionSettings.DesktopWidth)));
    }

    [Fact]
    public void Unset_everywhere_falls_back_to_the_built_in_default()
    {
        (_, _, _, ServerNode server) = BuildTree();

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.Equal(3389, effective.Values.Port);
        Assert.Null(effective.SourceOf(nameof(ConnectionSettings.Port)));
        Assert.Equal(SettingOrigin.Default, effective.OriginOf(nameof(ConnectionSettings.Port)));
        Assert.Equal("Default", effective.DescribeOrigin(nameof(ConnectionSettings.Port)));
    }

    /// <summary>
    /// False is a real override, not an absence. Getting this wrong would make
    /// it impossible to turn a setting off below a group that turned it on —
    /// the classic bug in nullable-means-inherit designs.
    /// </summary>
    [Fact]
    public void False_overrides_an_inherited_true()
    {
        (_, GroupNode prod, _, ServerNode server) = BuildTree();
        prod.Settings.RedirectClipboard = true;
        server.Settings.RedirectClipboard = false;

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.False(effective.Values.RedirectClipboard);
        Assert.Same(server, effective.SourceOf(nameof(ConnectionSettings.RedirectClipboard)));
    }

    /// <summary>Zero is likewise a value, not an absence.</summary>
    [Fact]
    public void Zero_overrides_an_inherited_number()
    {
        (_, GroupNode prod, _, ServerNode server) = BuildTree();
        prod.Settings.ConnectTimeoutSeconds = 30;
        server.Settings.ConnectTimeoutSeconds = 0;

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.Equal(0, effective.Values.ConnectTimeoutSeconds);
        Assert.Same(server, effective.SourceOf(nameof(ConnectionSettings.ConnectTimeoutSeconds)));
    }

    [Fact]
    public void Clearing_an_override_restores_inheritance()
    {
        (_, GroupNode prod, _, ServerNode server) = BuildTree();
        prod.Settings.UserName = @"CORP\svc_rdadmin";
        server.Settings.UserName = @"CORP\sql_admin";

        Assert.True(SettingsResolver.HasOverride(server, nameof(ConnectionSettings.UserName)));

        SettingsResolver.ClearOverride(server, nameof(ConnectionSettings.UserName));

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        Assert.False(SettingsResolver.HasOverride(server, nameof(ConnectionSettings.UserName)));
        Assert.Equal(@"CORP\svc_rdadmin", effective.Values.UserName);
        Assert.Same(prod, effective.SourceOf(nameof(ConnectionSettings.UserName)));
    }

    [Fact]
    public void Clearing_an_unknown_setting_is_rejected()
    {
        (_, _, _, ServerNode server) = BuildTree();

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => SettingsResolver.ClearOverride(server, "NotASetting"));

        Assert.Contains("NotASetting", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_resolves_the_same_way_a_server_does()
    {
        (_, GroupNode prod, GroupNode web, _) = BuildTree();
        prod.Settings.ColourDepth = ColourDepth.TrueColour24;

        EffectiveSettings effective = SettingsResolver.Resolve(web);

        Assert.Equal(ColourDepth.TrueColour24, effective.Values.ColourDepth);
        Assert.Same(prod, effective.SourceOf(nameof(ConnectionSettings.ColourDepth)));
    }

    /// <summary>
    /// A node with no parent links — for example one straight out of the
    /// deserialiser before RebuildParentLinks runs — must still resolve rather
    /// than throw. It gets defaults, which is wrong but recoverable; throwing
    /// here would take the whole document down.
    /// </summary>
    [Fact]
    public void An_orphaned_node_resolves_to_defaults()
    {
        ServerNode orphan = new() { Name = "LOOSE-01", HostName = "10.0.0.1" };

        EffectiveSettings effective = SettingsResolver.Resolve(orphan);

        Assert.Equal(3389, effective.Values.Port);
        Assert.Equal(SettingOrigin.Default, effective.OriginOf(nameof(ConnectionSettings.Port)));
    }

    [Fact]
    public void Custom_defaults_replace_the_built_in_ones()
    {
        (_, _, _, ServerNode server) = BuildTree();
        ConnectionSettings defaults = ConnectionSettings.Defaults;
        defaults.Port = 3390;

        EffectiveSettings effective = SettingsResolver.Resolve(server, defaults);

        Assert.Equal(3390, effective.Values.Port);
        Assert.Equal(SettingOrigin.Default, effective.OriginOf(nameof(ConnectionSettings.Port)));
    }

    /// <summary>
    /// The guard that keeps the reflective resolver honest. Fills every
    /// property on an ancestor and checks all of them reach the leaf, so a
    /// newly added setting that somehow escapes resolution fails here rather
    /// than resolving to null in production.
    /// </summary>
    [Fact]
    public void Every_setting_participates_in_inheritance()
    {
        (_, GroupNode prod, _, ServerNode server) = BuildTree();
        prod.Settings = FullyPopulatedSettings();

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        List<string> notInherited =
        [
            .. SettingsResolver.InheritableProperties
                .Where(name => effective.OriginOf(name) != SettingOrigin.Inherited)
        ];

        Assert.True(
            notInherited.Count == 0,
            "These settings did not inherit from an ancestor that sets them: "
            + string.Join(", ", notInherited)
            + ". Every property on ConnectionSettings must be nullable and reachable by the resolver.");
    }

    /// <summary>
    /// Most settings have a built-in default; a few genuinely do not, because
    /// there is no sensible default user name or gateway. This pins that list
    /// so adding a setting forces a decision — give it a default, or add it to
    /// <see cref="ConnectionSettings.WithoutDefaults"/> — instead of it
    /// silently resolving to null and surfacing as an empty field much later.
    /// </summary>
    [Fact]
    public void Settings_without_a_default_are_the_expected_ones()
    {
        ConnectionSettings defaults = ConnectionSettings.Defaults;

        HashSet<string> actuallyNull =
        [
            .. typeof(ConnectionSettings)
                .GetProperties()
                .Where(p => p.CanWrite && p.GetValue(defaults) is null)
                .Select(p => p.Name)
        ];

        Assert.Equal(
            ConnectionSettings.WithoutDefaults.OrderBy(n => n, StringComparer.Ordinal),
            actuallyNull.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Resolution_populates_everything_that_has_a_default()
    {
        (_, _, _, ServerNode server) = BuildTree();

        EffectiveSettings effective = SettingsResolver.Resolve(server);

        List<string> unexpectedlyNull =
        [
            .. typeof(ConnectionSettings)
                .GetProperties()
                .Where(p => p.CanWrite
                    && !ConnectionSettings.WithoutDefaults.Contains(p.Name)
                    && p.GetValue(effective.Values) is null)
                .Select(p => p.Name)
        ];

        Assert.True(
            unexpectedlyNull.Count == 0,
            "These settings claim to have a default but resolved to null: "
            + string.Join(", ", unexpectedlyNull));
    }

    /// <summary>
    /// A settings object with every property set, whatever its type. Used to
    /// prove the resolver reaches all of them without depending on which ones
    /// happen to have defaults.
    /// </summary>
    private static ConnectionSettings FullyPopulatedSettings()
    {
        ConnectionSettings settings = new();

        foreach (System.Reflection.PropertyInfo property in
            typeof(ConnectionSettings).GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            object value = type switch
            {
                _ when type == typeof(string) => "set-for-test",
                _ when type == typeof(int) => 4242,
                _ when type == typeof(bool) => true,
                _ when type == typeof(Guid) => Guid.NewGuid(),
                _ when type.IsEnum => Enum.GetValues(type).GetValue(0)!,
                _ => throw new NotSupportedException(
                    $"{nameof(FullyPopulatedSettings)} does not know how to fill "
                    + $"{property.Name} of type {type.Name}. Add a case for it."),
            };

            property.SetValue(settings, value);
        }

        return settings;
    }
}
