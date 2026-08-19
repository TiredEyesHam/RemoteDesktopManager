namespace Patchbay.Core.Sessions;

/// <summary>
/// A session could not be established, or dropped in a way that was not
/// asked for. <see cref="Exception.Message"/> is written to be shown to
/// someone, not just logged.
///
/// The RDP control reports failures as numeric disconnect reasons; turning
/// those into sentences is M4-07, and it produces this.
/// </summary>
public class RemoteSessionException : Exception
{
    public RemoteSessionException()
    {
    }

    public RemoteSessionException(string message)
        : base(message)
    {
    }

    public RemoteSessionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
