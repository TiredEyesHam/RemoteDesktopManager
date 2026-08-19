using System.Reflection;
using System.Runtime.Versioning;
using Patchbay.Core;

namespace Patchbay.Tests;

/// <summary>
/// Patchbay.Core must stay platform-neutral. It holds the domain model,
/// inheritance resolution, storage and the importers — none of which have any
/// business touching WPF, WinForms or COM. Keeping that true is what makes a
/// non-Windows shell possible later (M8-19), and it is far easier to defend
/// with a test than with a code review.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(AssemblyMarker).Assembly;

    private static readonly string[] ForbiddenReferences =
    [
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Windows.Forms",
        "System.Drawing.Common",
        "Microsoft.Win32.Registry",
    ];

    [Fact]
    public void Core_targets_a_platform_neutral_framework()
    {
        string? framework = Core.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        Assert.NotNull(framework);
        Assert.DoesNotContain("windows", framework, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Core_does_not_reference_ui_or_windows_only_assemblies()
    {
        string[] referenced = [.. Core.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];

        string[] violations =
        [
            .. ForbiddenReferences.Where(f =>
                referenced.Contains(f, StringComparer.OrdinalIgnoreCase))
        ];

        Assert.True(
            violations.Length == 0,
            $"Patchbay.Core must stay platform-neutral, but it references: {string.Join(", ", violations)}. "
            + "Move the offending code to Patchbay.Rdp, or put it behind an interface that Core owns "
            + "and Patchbay.App implements.");
    }
}
