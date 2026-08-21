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

    // ── Secrets do not print (M3-11) ────────────────────────────────────

    [Fact]
    public void Anything_holding_a_secret_overrides_ToString()
    {
        // The threat model's central claim, held to rather than written down.
        // A record's generated ToString prints every property it has, which is
        // the likeliest way a password reaches a log file — through a line of
        // code nobody wrote. Adding a type with a Password on it and no
        // override fails here rather than in somebody's support ticket.
        string[] telltale = ["Password", "Secret", "ProtectedPassword"];

        List<string> offenders = [];
        int examined = 0;

        foreach (Type type in Core.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            bool holdsOne = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(p => p.PropertyType == typeof(string)
                    && telltale.Any(n => p.Name.Contains(n, StringComparison.Ordinal)));

            if (!holdsOne)
            {
                continue;
            }

            examined++;

            MethodInfo? declared = type.GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                binder: null,
                types: [],
                modifiers: null);

            if (declared is null)
            {
                offenders.Add(type.Name);
            }
        }

        Assert.Empty(offenders);

        // Guards against the rule passing because it found nothing to check —
        // a renamed property would otherwise turn this into a test of the
        // empty set.
        Assert.True(examined >= 3, $"Only {examined} secret-holding types were examined.");
    }
}
