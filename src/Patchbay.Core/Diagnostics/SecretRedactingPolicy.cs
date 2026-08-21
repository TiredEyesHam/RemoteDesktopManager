using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Serilog.Core;
using Serilog.Events;

namespace Patchbay.Core.Diagnostics;

/// <summary>
/// Stops a secret reaching a log through <c>{@Object}</c> (M3-08).
///
/// <para>
/// Serilog has two ways of turning an object into a log property.
/// <c>{Thing}</c> keeps the object and renders it with <c>ToString</c>, which
/// is already safe: every Patchbay type that can hold a secret overrides
/// <c>ToString</c> to redact it, and
/// <c>ArchitectureTests.Anything_holding_a_secret_overrides_ToString</c> fails
/// if a new one does not. <c>{@Thing}</c> destructures instead, reflecting
/// over every property and never calling <c>ToString</c> at all. That second
/// route goes straight past the guarantee, and it is the one somebody reaches
/// for precisely when they want detail.
/// </para>
///
/// <para>
/// So this policy takes over destructuring for Patchbay's own types, and does
/// two things with them. A type with a <c>ToString</c> somebody wrote is
/// rendered with it rather than taken apart, because a type that bothered to
/// write one has already decided how it wants to be shown. That covers
/// <see cref="Sessions.RdpSettingWrite"/>, whose secret sits in a property
/// called <c>Value</c> that no name test would ever catch. Anything else is
/// destructured as usual, with members whose name looks like a secret replaced
/// by <see cref="SecretNames.Mask"/>.
/// </para>
///
/// <para>
/// "Somebody wrote" is doing real work in that sentence: see
/// <see cref="PrintsItself"/>. A record's synthesised <c>ToString</c> prints
/// every property it has, so trusting it would be worse than destructuring.
/// </para>
///
/// <para>
/// Types from outside Patchbay are left to Serilog. Redacting other people's
/// objects by reflection is a good way to break a diagnostic that somebody
/// else was relying on, and the secrets here all live in types this repository
/// owns.
/// </para>
/// </summary>
public sealed class SecretRedactingPolicy : IDestructuringPolicy
{
    /// <summary>
    /// Per-type decision, worked out once. A null entry means the type prints
    /// itself. This runs on every destructured property of every log event, so
    /// the reflection happens once per type rather than once per line.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]?> Plans = new();

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        result = null;

        if (value is null)
        {
            return false;
        }

        Type type = value.GetType();

        if (!IsOurs(type) || IsMany(value))
        {
            return false;
        }

        PropertyInfo[]? members = Plans.GetOrAdd(type, Plan);

        if (members is null)
        {
            result = new ScalarValue(value.ToString());
            return true;
        }

        List<LogEventProperty> properties = new(members.Length);

        foreach (PropertyInfo member in members)
        {
            properties.Add(new LogEventProperty(member.Name, Read(member, value, propertyValueFactory)));
        }

        result = new StructureValue(properties, type.Name);
        return true;
    }

    private static LogEventPropertyValue Read(
        PropertyInfo member,
        object owner,
        ILogEventPropertyValueFactory factory)
    {
        object? read;

        try
        {
            read = member.GetValue(owner);
        }
        catch (TargetInvocationException ex)
        {
            // A property that throws is worth saying so about. Dropping it
            // silently would leave a log that reads as though the member does
            // not exist.
            return new ScalarValue($"<{ex.InnerException?.GetType().Name ?? nameof(Exception)}>");
        }

        return SecretNames.Redacts(member.Name, read)
            ? new ScalarValue(SecretNames.Mask)
            : factory.CreatePropertyValue(read, destructureObjects: true);
    }

    /// <summary>
    /// Whether the type has a <c>ToString</c> somebody wrote on purpose.
    ///
    /// <para>
    /// The compiler-generated check is the whole of it. A <c>record</c> gets a
    /// <c>ToString</c> synthesised onto the type itself, so asking only
    /// whether one is declared here says yes for every record in the codebase
    /// — including one that prints <c>Password = hunter2</c>. The synthesised
    /// member carries <see cref="CompilerGeneratedAttribute"/> and a written
    /// one does not, which is the only thing that tells them apart.
    /// </para>
    ///
    /// <para>
    /// Public because
    /// <c>ArchitectureTests.Anything_holding_a_secret_overrides_ToString</c>
    /// asks the same question, and the two answers have to be the same one:
    /// the test decides which types must have an override, and this decides
    /// which types are trusted to use theirs.
    /// </para>
    /// </summary>
    public static bool PrintsItself(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        MethodInfo? declared = type.GetMethod(
            nameof(ToString),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: [],
            modifiers: null);

        return declared is not null
            && declared.GetCustomAttribute<CompilerGeneratedAttribute>() is null;
    }

    /// <summary>
    /// The members to destructure, or null when the type prints itself.
    /// </summary>
    private static PropertyInfo[]? Plan(Type type)
    {
        if (PrintsItself(type))
        {
            return null;
        }

        return [.. type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)];
    }

    /// <summary>
    /// Whether the type is Patchbay's to reason about. Namespace rather than
    /// assembly, so this holds for <c>Patchbay.Rdp</c> and <c>Patchbay.App</c>
    /// types too without Core having to reference either.
    /// </summary>
    private static bool IsOurs(Type type) =>
        type.Namespace?.StartsWith("Patchbay", StringComparison.Ordinal) is true;

    /// <summary>
    /// Whether the value is a collection, which this has no business taking
    /// apart.
    ///
    /// <para>
    /// An array of one of our types is one of our types by namespace, and
    /// destructuring it as an object produces <c>Length</c>, <c>Rank</c> and
    /// <c>SyncRoot</c> instead of the contents. Serilog already knows how to
    /// render a sequence, and each element comes back through here on its own.
    /// </para>
    /// </summary>
    private static bool IsMany(object value) => value is IEnumerable and not string;
}
