using System.Globalization;
using System.Text;

namespace Patchbay.Core.Import;

/// <summary>
/// The lines of a Remote Desktop <c>.rdp</c> file, read without trusting any
/// of them (M1-14).
///
/// The format is one setting per line, <c>name:type:value</c>, where the type
/// is a single letter: <c>s</c> for text, <c>i</c> for a number, <c>b</c> for
/// a hex blob. Names are case-insensitive and contain spaces. The value is
/// everything after the second colon, colons included, which is why an address
/// like <c>host:3390</c> survives.
///
/// <para>
/// <b>A binary value is not kept.</b> The only one that turns up in practice
/// is <c>password 51</c>, a DPAPI blob belonging to whoever saved the file,
/// and the surest way for it never to reach a node name, a warning or a log
/// line is for this class not to hold it. <see cref="Text"/> returns null for
/// a binary entry rather than the hex.
/// </para>
///
/// <para>
/// <b>A repeated name is recorded rather than resolved quietly.</b> The last
/// one wins, which is what <c>mstsc.exe</c> does, and both are worth knowing
/// about: nothing that writes these files repeats a setting, so a file that
/// does has either been edited by hand or been built to read one way and
/// behave another.
/// </para>
/// </summary>
public sealed class RdpFile
{
    /// <summary>
    /// Largest file accepted, in characters. A real <c>.rdp</c> is two or
    /// three kilobytes; a signed one carries a certificate and is still well
    /// under a hundred. This is generous enough that no honest file hits it
    /// and small enough that a hostile one cannot exhaust memory before it is
    /// refused.
    /// </summary>
    public const int MaxCharacters = 1024 * 1024;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _repeated = new(StringComparer.OrdinalIgnoreCase);

    private RdpFile()
    {
    }

    /// <summary>Names present in the file, in no particular order.</summary>
    public IReadOnlyCollection<string> Names => _entries.Keys;

    /// <summary>
    /// Names that appeared more than once with different values. Empty for
    /// every file a Remote Desktop client has written.
    /// </summary>
    public IReadOnlyCollection<string> RepeatedNames => _repeated;

    /// <summary>
    /// Lines that were not <c>name:type:value</c> and were skipped. Counted
    /// rather than reported: a stray line is ordinary, and it is the absence
    /// of an address that decides whether a file is usable.
    /// </summary>
    public int UnreadableLines { get; private set; }

    /// <summary>Reads a file, detecting its encoding from a byte order mark.</summary>
    /// <exception cref="ImportException">The stream is longer than <see cref="MaxCharacters"/>.</exception>
    public static RdpFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Detection matters here rather than being tidiness. mstsc.exe writes
        // UTF-16, and reading one of its files as UTF-8 produces a NUL between
        // every letter and a file that appears to hold no settings at all.
        using StreamReader reader = new(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        StringBuilder text = new();
        char[] buffer = new char[8192];
        int read;

        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (text.Length + read > MaxCharacters)
            {
                throw new ImportException(
                    "This file is far larger than any Remote Desktop file, so it has not been "
                    + "read.");
            }

            text.Append(buffer, 0, read);
        }

        return Read(text.ToString());
    }

    /// <summary>Reads text that has already been decoded.</summary>
    public static RdpFile Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        RdpFile file = new();

        foreach (string raw in text.Split('\n'))
        {
            file.ReadLine(raw.TrimEnd('\r'));
        }

        return file;
    }

    /// <summary>Whether a setting is present, whatever its type.</summary>
    public bool Has(string name) => _entries.ContainsKey(name);

    /// <summary>Whether a setting is present and holds a blob.</summary>
    public bool HasBinary(string name) =>
        _entries.TryGetValue(name, out Entry entry) && entry.Type == 'b';

    /// <summary>
    /// A setting's text, or null when it is absent, empty, or binary.
    /// </summary>
    public string? Text(string name)
    {
        if (!_entries.TryGetValue(name, out Entry entry) || entry.Type == 'b')
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(entry.Value) ? null : entry.Value;
    }

    /// <summary>
    /// A setting as a number, or null when it is absent or is not one. The
    /// declared type is not consulted: a file that says <c>s</c> and holds a
    /// number is readable, and refusing it would lose a setting over a letter.
    /// </summary>
    public int? Number(string name) =>
        Text(name) is { } value
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    /// <summary>
    /// A setting as a switch. Zero is off and every other number is on, which
    /// is how the client reads them.
    /// </summary>
    public bool? Flag(string name) => Number(name) is { } value ? value != 0 : null;

    private void ReadLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        // name:type:value — the first colon ends the name, the letter after it
        // is the type, and the colon after that begins a value which may hold
        // colons of its own.
        int colon = line.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 0 || line.Length < colon + 3 || line[colon + 2] != ':')
        {
            UnreadableLines++;
            return;
        }

        char type = char.ToLowerInvariant(line[colon + 1]);

        if (type is not ('s' or 'i' or 'b'))
        {
            UnreadableLines++;
            return;
        }

        string name = line[..colon].Trim();

        if (name.Length == 0)
        {
            UnreadableLines++;
            return;
        }

        // Nothing is kept for a blob beyond the fact that there was one.
        string value = type == 'b' ? string.Empty : line[(colon + 3)..].Trim();

        if (_entries.TryGetValue(name, out Entry existing)
            && !string.Equals(existing.Value, value, StringComparison.Ordinal))
        {
            _repeated.Add(name);
        }

        _entries[name] = new Entry(type, value);
    }

    private readonly record struct Entry(char Type, string Value);
}
