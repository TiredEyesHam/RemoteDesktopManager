namespace Patchbay.Core.Import;

/// <summary>
/// A file could not be imported. The message is written to be shown to a
/// person as-is: it says what was wrong with their file, not what the parser
/// was doing at the time.
/// </summary>
public sealed class ImportException : Exception
{
    public ImportException()
    {
    }

    public ImportException(string message)
        : base(message)
    {
    }

    public ImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
