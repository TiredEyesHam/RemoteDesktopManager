namespace Patchbay.Rdp.Interop;

/// <summary>
/// The RDP control could not be created, or refused something asked of it.
///
/// Kept distinct from <see cref="Patchbay.Core.Sessions.RemoteSessionException"/>
/// because the two mean different things to the person reading the message: a
/// session exception says a particular machine could not be reached, this one
/// says the RDP engine on <i>this</i> computer is missing or unusable and no
/// connection will work until it is fixed. The shell answers the second by
/// falling back to <see cref="Patchbay.Core.Sessions.FakeRemoteSessionHost"/>
/// and saying so plainly, rather than by offering a retry that cannot succeed.
/// </summary>
public sealed class RdpEngineException : Exception
{
    public RdpEngineException()
    {
    }

    public RdpEngineException(string message)
        : base(message)
    {
    }

    public RdpEngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
