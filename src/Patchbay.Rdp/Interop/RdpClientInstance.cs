using System.Globalization;
using System.Runtime.InteropServices;
using Patchbay.Core.Sessions;

namespace Patchbay.Rdp.Interop;

/// <summary>
/// One live RDP control, plus what the probe learned about it.
///
/// A thin thing on purpose. It owns the COM object and its release, answers
/// capability questions, and exposes the members every generation has. What it
/// does not do is map settings (M4-04), run a connection (M4-05) or put
/// anything on screen (M4-03). Those are separate tasks with separate risks,
/// and the seam is easier to keep honest if this type stays boring.
///
/// Not thread-safe, and deliberately not made so: the control belongs to the
/// STA thread that created it, and a lock here would only disguise a call that
/// arrived from the wrong one.
/// </summary>
public sealed class RdpClientInstance : IDisposable
{
    private readonly bool _ownsComObject;
    private object? _instance;

    internal RdpClientInstance(object instance, RdpEngineInfo engine, bool ownsComObject = true)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(engine);

        _instance = instance;
        _ownsComObject = ownsComObject;
        Engine = engine;
    }

    /// <summary>What this control turned out to be.</summary>
    public RdpEngineInfo Engine { get; }

    /// <summary>
    /// The underlying COM object, for the code that has to hand it to a
    /// hosting control (M4-03) or walk its settings (M4-04). Deliberately
    /// typed as <see cref="object"/>: there is no vtable-accurate interface to
    /// offer, and pretending otherwise is the mistake this whole layer avoids.
    /// </summary>
    public object ComObject => _instance ?? throw new ObjectDisposedException(nameof(RdpClientInstance));

    /// <summary>Whether the control is at least <paramref name="level"/>.</summary>
    public bool Supports(RdpClientLevel level) => Engine.Level >= level;

    /// <summary>
    /// The machine to connect to. Present on every generation, which is why
    /// the probe uses it as its proof of life.
    /// </summary>
    public string Server
    {
        get => RdpDispatch.Get<string>(ComObject, "Server") ?? string.Empty;
        set => RdpDispatch.Set(ComObject, "Server", value);
    }

    /// <summary>
    /// 0 disconnected, 1 connected, 2 connecting. Raw on purpose: turning it
    /// into a state machine is M4-05, and two representations of the same
    /// thing is how they drift apart.
    /// </summary>
    public int ConnectionState => RdpDispatch.Get<int>(ComObject, "Connected");

    /// <summary>
    /// Starts connecting. Returns at once — the control does the work on its
    /// own threads and reports what happens through its events (M4-06), so
    /// there is nothing here to wait on and nothing useful returned.
    /// </summary>
    /// <exception cref="RdpEngineException">
    /// The control refused to start, which is different from the connection
    /// failing: a refusal here means it was misconfigured or already busy.
    /// </exception>
    public void Connect() => RdpDispatch.Call(ComObject, "Connect");

    /// <summary>
    /// Ends the session. Also returns at once, and the end arrives as an
    /// <c>OnDisconnected</c> like any other.
    /// </summary>
    public void Disconnect() => RdpDispatch.Call(ComObject, "Disconnect");

    /// <summary>
    /// The second half of a disconnect reason (M4-07). Meaningful only once a
    /// session has ended, and read together with the reason the control
    /// announced — never on its own.
    /// </summary>
    public int ExtendedDisconnectReason => RdpDispatch.Get<int>(ComObject, "ExtendedDisconnectReason");

    /// <summary>
    /// The control's own words for a disconnect, or null when it has none
    /// (M4-07).
    ///
    /// <para>
    /// <b>Both numbers, always.</b> The reason from <c>OnDisconnected</c> says
    /// which family the ending belongs to and the extended reason says what
    /// actually happened, and the interesting cases live entirely in the
    /// second: reason 3 alone is "your session has ended, possibly for one of
    /// the following reasons" followed by three guesses, while reason 3 with
    /// extended reason 5 is "you have been disconnected because another
    /// connection was made to the remote computer". Passing one and not the
    /// other turns the answer back into the question.
    /// </para>
    ///
    /// <para>
    /// Worth preferring to anything Patchbay could write, because it is
    /// Microsoft's text for Microsoft's codes and it arrives in the language
    /// Windows is running in.
    /// </para>
    /// </summary>
    public string? DescribeDisconnect(int reason)
    {
        try
        {
            object? text = RdpDispatch.Call(
                ComObject, "GetErrorDescription", reason, ExtendedDisconnectReason);

            return SessionReasons.Tidy(text as string);
        }
        catch (RdpEngineException)
        {
            // An older control without the method, or one that would not
            // answer. The caller falls back to the code, which is a poorer
            // message and not a failure.
            return null;
        }
    }

    /// <summary>Reads a member by name. See <see cref="RdpDispatch"/> for why this is late-bound.</summary>
    public T? GetProperty<T>(string name) => RdpDispatch.Get<T>(ComObject, name);

    /// <summary>Writes a member by name.</summary>
    public void SetProperty(string name, object? value) => RdpDispatch.Set(ComObject, name, value);

    /// <summary>
    /// Fetches one of the settings objects hanging off the control, e.g.
    /// <c>AdvancedSettings9</c>. The generation-specific names are M4-04's
    /// problem; this only gets hold of them.
    /// </summary>
    public object GetSettings(string name) => RdpDispatch.GetObject(ComObject, name);

    /// <summary>Whether this control has a member at all.</summary>
    public bool Has(string name) => RdpDispatch.Has(ComObject, name);

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{nameof(RdpClientInstance)}({Engine.Description})");

    /// <summary>
    /// Releases the control. Deterministic rather than left to the finaliser
    /// because each one holds a socket, a decoder and a chunk of bitmap cache,
    /// and a tab that is closed should stop costing that immediately rather
    /// than whenever a collection happens to run.
    ///
    /// When the object came from a hosting control (M4-03) the host owns it,
    /// and this only drops the reference — releasing an OCX that WinForms is
    /// still holding would take the window down with it.
    /// </summary>
    public void Dispose()
    {
        object? held = _instance;
        _instance = null;

        if (!_ownsComObject || held is null || !Marshal.IsComObject(held))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(held);
        }
        catch (ArgumentException)
        {
            // Someone got there first. Disposing twice is not an error worth raising.
        }
    }
}
