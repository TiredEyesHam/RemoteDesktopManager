using System.Runtime.InteropServices;

namespace Patchbay.App.Interop;

/// <summary>
/// Windows Credential Manager, as five functions (M3-04).
///
/// <para>
/// Nothing here decides anything. Which entries exist, what they are called
/// and when one should go are all in
/// <c>Patchbay.App.Security.CredentialManagerSecretProtector</c> and, above
/// that, in <c>Core</c> where there are tests. What is left here is the part
/// that cannot be tested without Windows, and it is deliberately the smallest
/// part: copy bytes in, copy bytes out, write over the copies.
/// </para>
///
/// <para>
/// <b>The blobs are passwords, so the memory is handled as such.</b> Every
/// buffer holding one is written over before it is released, and none is ever
/// turned into a <see cref="string"/> — the same discipline as M3-03, applied
/// on the far side of the interop boundary where the garbage collector cannot
/// help. Enumeration is the awkward one: Windows hands the credentials back
/// with their blobs attached whether or not they were asked for, so the blobs
/// are written over in place before the buffer goes back. Skipping that would
/// drag every saved password through the process on an operation whose entire
/// purpose is counting.
/// </para>
/// </summary>
internal static class WindowsCredentials
{
    /// <summary>An application's own credential, opaque to Windows.</summary>
    internal const int GenericCredential = 1;

    /// <summary>
    /// Kept on this machine, across sign-outs, and nowhere else.
    ///
    /// <para>
    /// The alternative is <c>CRED_PERSIST_ENTERPRISE</c>, which roams the
    /// credential with a domain profile, and it is not chosen. Everything
    /// Patchbay saves is protected against travelling — DPAPI's
    /// <c>CurrentUser</c> scope does not leave the machine either — and a
    /// store that quietly pushed saved passwords into roaming profile storage
    /// would be a different security claim, made without anybody being asked.
    /// A password that is meant to travel is what the master password is for
    /// (M3-07).
    /// </para>
    /// </summary>
    internal const int PersistLocalMachine = 2;

    /// <summary>The most a generic credential's blob may be: 5 x 512 bytes.</summary>
    internal const int MaxBlobLength = 2560;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int BlobSize;
        public IntPtr Blob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    /// <summary>
    /// Writes <paramref name="blob"/> under <paramref name="target"/>,
    /// replacing whatever was there. False when Windows refused — a policy
    /// against storing credentials, a full store, an account with no profile
    /// loaded.
    /// </summary>
    internal static bool TryWrite(string target, ReadOnlySpan<byte> blob, string userName)
    {
        if (blob.Length is 0 or > MaxBlobLength)
        {
            return false;
        }

        byte[] copy = GC.AllocateArray<byte>(blob.Length, pinned: true);
        IntPtr bytes = Marshal.AllocHGlobal(blob.Length);
        IntPtr name = IntPtr.Zero;
        IntPtr user = IntPtr.Zero;

        try
        {
            // The span is only lent, and Marshal.Copy wants an array, so this
            // is the one copy it takes — pinned, and written over below.
            blob.CopyTo(copy);
            Marshal.Copy(copy, 0, bytes, blob.Length);

            name = Marshal.StringToHGlobalUni(target);
            user = Marshal.StringToHGlobalUni(userName);

            Credential credential = new()
            {
                Type = GenericCredential,
                TargetName = name,
                Blob = bytes,
                BlobSize = blob.Length,
                Persist = PersistLocalMachine,
                UserName = user,
            };

            return CredWriteW(ref credential, 0);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(copy);
            Zero(bytes, blob.Length);
            Marshal.FreeHGlobal(bytes);

            if (name != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(name);
            }

            if (user != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(user);
            }
        }
    }

    /// <summary>
    /// Reads the blob stored under <paramref name="target"/>. False when there
    /// is nothing there, which is ordinary: a document that has moved to
    /// another machine refers to entries that were never on it.
    /// </summary>
    /// <remarks>
    /// The array that comes back holds a password. Writing over it is the
    /// caller's.
    /// </remarks>
    internal static bool TryRead(string target, out byte[]? blob)
    {
        blob = null;

        if (!CredReadW(target, GenericCredential, 0, out IntPtr handle))
        {
            return false;
        }

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(handle);

            if (credential.Blob == IntPtr.Zero || credential.BlobSize <= 0)
            {
                return false;
            }

            // Pinned, so that what is about to be written over is where it was
            // put rather than wherever a collection moved it to (M3-03).
            blob = GC.AllocateArray<byte>(credential.BlobSize, pinned: true);
            Marshal.Copy(credential.Blob, blob, 0, credential.BlobSize);

            Zero(credential.Blob, credential.BlobSize);

            return true;
        }
        finally
        {
            CredFree(handle);
        }
    }

    /// <summary>
    /// Removes an entry. False when it was not there, which is not a failure:
    /// being asked to forget something twice is ordinary.
    /// </summary>
    internal static bool Delete(string target) => CredDeleteW(target, GenericCredential, 0);

    /// <summary>
    /// The target names matching <paramref name="filter"/>, which may end in
    /// an asterisk. Empty when there are none.
    /// </summary>
    internal static List<string> Names(string filter)
    {
        List<string> names = [];

        if (!CredEnumerateW(filter, 0, out int count, out IntPtr array))
        {
            return names;
        }

        try
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr entry = Marshal.ReadIntPtr(array, i * IntPtr.Size);

                if (entry == IntPtr.Zero)
                {
                    continue;
                }

                Credential credential = Marshal.PtrToStructure<Credential>(entry);

                // Windows returns the blobs whether or not they were wanted,
                // and this call never wants them. Written over here rather
                // than left sitting in the buffer until CredFree gets to it.
                Zero(credential.Blob, credential.BlobSize);

                if (credential.TargetName != IntPtr.Zero
                    && Marshal.PtrToStringUni(credential.TargetName) is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
        }
        finally
        {
            CredFree(array);
        }

        return names;
    }

    private static void Zero(IntPtr buffer, int length)
    {
        if (buffer == IntPtr.Zero || length <= 0)
        {
            return;
        }

        Marshal.Copy(new byte[length], 0, buffer, length);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(
        [MarshalAs(UnmanagedType.LPWStr)] string target,
        int type,
        int flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(
        [MarshalAs(UnmanagedType.LPWStr)] string target,
        int type,
        int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredEnumerateW(
        [MarshalAs(UnmanagedType.LPWStr)] string? filter,
        int flags,
        out int count,
        out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
