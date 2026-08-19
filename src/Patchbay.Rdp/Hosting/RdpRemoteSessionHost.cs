using Patchbay.Core.Sessions;
using Patchbay.Rdp.Interop;

namespace Patchbay.Rdp.Hosting;

/// <summary>
/// The real session host: the other side of the seam
/// <see cref="FakeRemoteSessionHost"/> has been standing in for since M4-01.
///
/// <para>
/// There is almost nothing to it, and that is the whole return on the
/// abstraction. Everything the shell does with sessions — opening tabs,
/// promoting the next one when the front one closes, drawing a state, offering
/// a retry — was built and tested against the fake, and swapping this in
/// changes one argument at <c>App</c> startup and nothing else.
/// </para>
///
/// <para>
/// <see cref="TryCreate"/> rather than a constructor that throws, because "no
/// usable RDP control on this machine" is a thing Patchbay has to keep working
/// through: the tree, the editor and the import are all still worth having,
/// and the fake says plainly that nothing is really connected.
/// </para>
/// </summary>
public sealed class RdpRemoteSessionHost : IRemoteSessionHost
{
    private readonly RdpEngineInfo _engine;

    private RdpRemoteSessionHost(RdpEngineInfo engine)
    {
        _engine = engine;
    }

    /// <summary>What the control turned out to be, for About and for logs.</summary>
    public RdpEngineInfo Engine => _engine;

    /// <inheritdoc />
    public string Description => _engine.Description;

    /// <inheritdoc />
    public bool IsSimulated => false;

    /// <summary>
    /// The real host, or null when this machine has no control Patchbay can
    /// use. The probe has already proved the class id creatable by creating
    /// one, so a host that exists is one that works.
    /// </summary>
    /// <param name="refresh">Probe again rather than reusing the first answer.</param>
    public static RdpRemoteSessionHost? TryCreate(bool refresh = false)
        => RdpEngineProbe.Detect(refresh).Engine is { } engine ? new RdpRemoteSessionHost(engine) : null;

    /// <inheritdoc />
    public IRemoteSession CreateSession(SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A control and a window each. Nothing is connected until someone asks,
        // so a tab can exist — and be looked at, and be closed again — without
        // a socket ever being opened.
        return new RdpRemoteSession(request, _engine);
    }
}
