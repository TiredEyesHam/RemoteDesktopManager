using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

public class SettingCatalogueTests
{
    /// <summary>
    /// The guard that makes the catalogue worth having. Add a property to
    /// ConnectionSettings without describing it and this fails, rather than
    /// the setting quietly never appearing on screen.
    /// </summary>
    [Fact]
    public void Every_setting_is_described_exactly_once()
    {
        string[] described = [.. SettingCatalogue.All.Select(d => d.PropertyName)];

        Assert.Equal(
            [.. SettingsResolver.InheritableProperties.Order(StringComparer.Ordinal)],
            [.. described.Order(StringComparer.Ordinal)]);

        Assert.Equal(described.Length, described.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Descriptions_are_grouped_so_a_section_is_never_split()
    {
        List<string> runs = [];

        foreach (string section in SettingCatalogue.All.Select(d => d.Section))
        {
            if (runs.Count == 0 || !string.Equals(runs[^1], section, StringComparison.Ordinal))
            {
                runs.Add(section);
            }
        }

        Assert.Equal(SettingCatalogue.Sections, runs);
    }

    [Fact]
    public void A_choice_setting_describes_an_enum()
    {
        foreach (SettingDescriptor descriptor in SettingCatalogue.All)
        {
            if (descriptor.Kind is SettingKind.Choice)
            {
                Assert.True(descriptor.ValueType.IsEnum, descriptor.PropertyName);
            }
        }
    }

    /// <summary>
    /// The declared value type has to match the property, or the editor builds
    /// the wrong control and the cast fails at the point of saving.
    /// </summary>
    [Fact]
    public void The_declared_type_matches_the_property()
    {
        foreach (SettingDescriptor descriptor in SettingCatalogue.All)
        {
            Type declared = typeof(ConnectionSettings)
                .GetProperty(descriptor.PropertyName)!
                .PropertyType;

            Assert.Equal(descriptor.ValueType, Nullable.GetUnderlyingType(declared) ?? declared);
        }
    }

    [Fact]
    public void Reading_and_writing_by_name_round_trips()
    {
        ConnectionSettings settings = new();

        SettingCatalogue.Write(settings, nameof(ConnectionSettings.Port), 3390);
        Assert.Equal(3390, settings.Port);
        Assert.Equal(3390, SettingCatalogue.Read(settings, nameof(ConnectionSettings.Port)));

        // Null is how the editor clears an override.
        SettingCatalogue.Write(settings, nameof(ConnectionSettings.Port), null);
        Assert.Null(settings.Port);
    }

    [Fact]
    public void An_unknown_setting_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SettingCatalogue.For("Nope"));
        Assert.Throws<ArgumentException>(() => SettingCatalogue.Read(new ConnectionSettings(), "Nope"));
    }

    [Fact]
    public void The_credential_profile_is_the_only_setting_not_hand_editable() =>
        Assert.Equal(
            [nameof(ConnectionSettings.CredentialProfileId)],
            [.. SettingCatalogue.All.Where(d => d.Kind is SettingKind.Hidden).Select(d => d.PropertyName)]);

    // ── The eight groups (M1-03) ────────────────────────────────────────

    [Fact]
    public void The_sections_are_the_eight_that_were_planned_for_in_the_order_planned()
    {
        // Not alphabetical, and not the order the properties happen to be
        // declared in: it runs from what a new entry needs before it will work
        // at all to what most people never open.
        Assert.Equal(
            [
                "Connection",
                "Credentials",
                "Gateway",
                "Display",
                "Local resources",
                "Experience",
                "Security",
                "Advanced",
            ],
            SettingCatalogue.Sections);
    }

    [Fact]
    public void Every_section_has_something_in_it()
    {
        // A heading with nothing under it is a group somebody planned for and
        // then never filled, and it looks on screen exactly like a bug.
        foreach (string section in SettingCatalogue.Sections)
        {
            Assert.Contains(SettingCatalogue.Editable, d => d.Section == section);
        }
    }

    [Fact]
    public void The_section_names_are_the_constants_and_not_repeated_strings()
    {
        // So that renaming a heading is one edit rather than a search.
        Assert.Contains(SettingCatalogue.ExperienceSection, SettingCatalogue.Sections);
        Assert.Contains(SettingCatalogue.SecuritySection, SettingCatalogue.Sections);
        Assert.Contains(SettingCatalogue.AdvancedSection, SettingCatalogue.Sections);
    }
}
