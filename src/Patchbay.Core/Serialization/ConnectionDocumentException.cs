namespace Patchbay.Core.Serialization;

/// <summary>
/// A connection document could not be read. The message is written to be shown
/// to a person: it says what went wrong and what to do about it, because this
/// is the one error someone hits when their entire connection list will not
/// open.
/// </summary>
public class ConnectionDocumentException : Exception
{
    public ConnectionDocumentException()
    {
    }

    public ConnectionDocumentException(string message)
        : base(message)
    {
    }

    public ConnectionDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
