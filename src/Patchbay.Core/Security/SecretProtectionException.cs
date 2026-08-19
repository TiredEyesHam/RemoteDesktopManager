namespace Patchbay.Core.Security;

/// <summary>
/// A secret could not be protected, so it must not be stored (M3-02).
///
/// This is an exception rather than a result on purpose, and it is the one
/// direction that is. Failing to <em>read</em> a password is a nuisance
/// someone works around by typing it again; failing to <em>write</em> one and
/// carrying on regardless is how a plaintext password ends up in a file, or
/// how someone believes a password is saved when it is not. Neither can be
/// allowed to be ignorable, so the write path throws.
/// </summary>
public class SecretProtectionException : Exception
{
    public SecretProtectionException()
    {
    }

    public SecretProtectionException(string message)
        : base(message)
    {
    }

    public SecretProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
