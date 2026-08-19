using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Patchbay.Rdp.Interop;

/// <summary>
/// Reads and writes members of the RDP control by name, through IDispatch.
///
/// This is the other half of the decision recorded in
/// <c>RdpComInterfaces.cs</c>: the interfaces carry no vtable, so everything
/// goes out late-bound. In exchange, a member that a given control generation
/// does not have throws a named, catchable error at the point of use instead of
/// dereferencing whatever happens to sit in that vtable slot — which is what
/// makes it safe for the settings mapper (M4-04) to walk a large property
/// surface across four control generations without a per-generation table.
///
/// The cost is that member names are strings and the compiler cannot check
/// them, so anything spelt wrongly surfaces at run time. That is a real cost,
/// and it is why <see cref="Get{T}"/> and the rest turn a miss into a message
/// naming the member rather than letting a raw <see cref="COMException"/> out.
/// </summary>
internal static class RdpDispatch
{
    private const BindingFlags GetProperty = BindingFlags.GetProperty;
    private const BindingFlags SetProperty = BindingFlags.SetProperty;
    private const BindingFlags CallMethod = BindingFlags.InvokeMethod;

    /// <summary>Reads a property, e.g. <c>Server</c> or <c>ConnectedStatusText</c>.</summary>
    internal static T? Get<T>(object target, string name)
    {
        object? value = Dispatch(target, name, GetProperty, []);

        if (value is null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        // Automation is VARIANT-typed, and the control does not always use the
        // width its documentation implies. Connected is written up as a long
        // and arrives as VT_I2, so a plain cast to int throws an
        // InvalidCastException that reads like a Patchbay bug and is not one.
        // Convert instead, and only complain when the value genuinely will not
        // fit the shape the caller asked for.
        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new RdpEngineException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{name}' returned {value.GetType().Name}, which will not convert to {typeof(T).Name}."),
                ex);
        }
    }

    /// <summary>
    /// Reads a property that returns another COM object, e.g.
    /// <c>AdvancedSettings9</c> or <c>SecuredSettings3</c>. Distinct from
    /// <see cref="Get{T}"/> only so callers get a clear failure rather than a
    /// null reference two lines later.
    /// </summary>
    internal static object GetObject(object target, string name)
        => Dispatch(target, name, GetProperty, [])
            ?? throw new RdpEngineException(
                $"The RDP control returned nothing for '{name}', which should always be an object.");

    /// <summary>Writes a property.</summary>
    internal static void Set(object target, string name, object? value)
        => Dispatch(target, name, SetProperty, [value]);

    /// <summary>Calls a method, e.g. <c>Connect</c> or <c>Disconnect</c>.</summary>
    internal static object? Call(object target, string name, params object?[] arguments)
        => Dispatch(target, name, CallMethod, arguments);

    /// <summary>
    /// Whether the control has a member at all. Used to tell "this generation
    /// never had that setting" apart from "that setting was rejected", which
    /// are different conversations to have with the person.
    /// </summary>
    internal static bool Has(object target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            target.GetType().InvokeMember(name, GetProperty, binder: null, target, [], CultureInfo.InvariantCulture);
            return true;
        }
        catch (MissingMemberException)
        {
            return false;
        }
        catch (COMException ex) when (ex.HResult == DispIdUnknown)
        {
            return false;
        }
        catch (TargetInvocationException)
        {
            // It exists; it simply objected to being read right now. That is
            // still an answer to the question asked.
            return true;
        }
    }

    /// <summary>DISP_E_UNKNOWNNAME — the control has no member by that name.</summary>
    private const int DispIdUnknown = unchecked((int)0x80020006);

    private static object? Dispatch(object target, string name, BindingFlags flags, object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            return target.GetType().InvokeMember(
                name,
                flags,
                binder: null,
                target,
                arguments,
                CultureInfo.InvariantCulture);
        }
        catch (MissingMemberException ex)
        {
            throw new RdpEngineException(Unsupported(name), ex);
        }
        catch (COMException ex) when (ex.HResult == DispIdUnknown)
        {
            throw new RdpEngineException(Unsupported(name), ex);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // The control itself failed. Its own message is the useful part;
            // the reflection wrapper around it is not.
            throw new RdpEngineException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The RDP control rejected '{name}': {ex.InnerException.Message}"),
                ex.InnerException);
        }
        catch (COMException ex)
        {
            throw new RdpEngineException(
                string.Create(CultureInfo.InvariantCulture, $"'{name}' failed with HRESULT 0x{ex.HResult:X8}."),
                ex);
        }
    }

    private static string Unsupported(string name) => string.Create(
        CultureInfo.InvariantCulture,
        $"This RDP control has no '{name}'. It is an older generation than the one that introduced it.");
}
