using System.Net;
using System.Net.Sockets;
using Patchbay.Core.Model;

namespace Patchbay.Core.Validation;

/// <summary>
/// Checks what someone has typed before it reaches the document.
///
/// This lives in Core rather than in the editor view model so it can be tested
/// without a window, and so the importers (M1-13 onwards) can run the same
/// rules over a file full of someone else's data instead of inventing their
/// own idea of a valid host name.
/// </summary>
public static class NodeValidator
{
    /// <summary>Field names used in <see cref="ValidationIssue.Field"/>.</summary>
    public const string NameField = "Name";
    public const string HostNameField = "HostName";
    public const string PortField = "Port";

    private const int MaxHostNameLength = 253;
    private const int MaxLabelLength = 63;

    /// <summary>
    /// Validates a server about to be created or saved.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="hostName">Host name or IP address.</param>
    /// <param name="port">Port, or null to inherit it.</param>
    /// <param name="parent">Group the server will sit in, for the name clash check.</param>
    /// <param name="editing">
    /// The node being edited, so it does not clash with itself. Null when new.
    /// </param>
    public static IReadOnlyList<ValidationIssue> ValidateServer(
        string? name,
        string? hostName,
        int? port,
        GroupNode? parent = null,
        ConnectionNode? editing = null)
    {
        List<ValidationIssue> issues = [];

        ValidateName(name, parent, editing, issues);

        if (string.IsNullOrWhiteSpace(hostName))
        {
            issues.Add(new ValidationIssue(HostNameField, "Enter a host name or IP address."));
        }
        else if (!IsValidHost(hostName))
        {
            issues.Add(new ValidationIssue(
                HostNameField,
                $"'{hostName.Trim()}' is not a valid host name or IP address."));
        }

        if (port is not null && !IsValidPort(port.Value))
        {
            issues.Add(new ValidationIssue(PortField, "Port must be between 1 and 65535."));
        }

        return issues;
    }

    /// <summary>Validates a group about to be created or renamed.</summary>
    public static IReadOnlyList<ValidationIssue> ValidateGroup(
        string? name,
        GroupNode? parent = null,
        ConnectionNode? editing = null)
    {
        List<ValidationIssue> issues = [];
        ValidateName(name, parent, editing, issues);
        return issues;
    }

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    /// <summary>
    /// Whether a string is usable as an RDP target: a DNS name, an IPv4
    /// address, or an IPv6 address.
    ///
    /// Deliberately permissive about underscores, which are invalid per RFC
    /// 1123 but turn up constantly in real Active Directory estates. Rejecting
    /// a name that <c>mstsc.exe</c> connects to happily would be a bug, not
    /// rigour.
    /// </summary>
    public static bool IsValidHost(string? hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return false;
        }

        string host = hostName.Trim();

        // IPv6 may arrive bracketed, the way it appears in a URL.
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }

        // TryParse accepts strings nobody means as an address: a bare "12345"
        // is read as an integer IPv4 and becomes 0.0.48.57. Requiring the
        // round trip to match rules those out. A failure here is not a
        // rejection though — it just means this is not an address, so the DNS
        // name rules below get their turn, and a machine called 12345 works.
        if (IPAddress.TryParse(host, out IPAddress? address)
            && (address.AddressFamily is AddressFamily.InterNetworkV6
                || string.Equals(address.ToString(), host, StringComparison.Ordinal)))
        {
            return true;
        }

        if (host.Length > MaxHostNameLength)
        {
            return false;
        }

        // A trailing dot is a legal fully-qualified name; drop it before
        // splitting so it does not produce an empty final label.
        if (host.EndsWith('.'))
        {
            host = host[..^1];
        }

        string[] labels = host.Split('.');

        return labels.Length > 0 && Array.TrueForAll(labels, IsValidLabel);
    }

    private static bool IsValidLabel(string label)
    {
        if (label.Length is 0 or > MaxLabelLength)
        {
            return false;
        }

        if (label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (char c in label)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateName(
        string? name,
        GroupNode? parent,
        ConnectionNode? editing,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new ValidationIssue(NameField, "Enter a name."));
            return;
        }

        if (parent is null)
        {
            return;
        }

        bool clash = parent.Children.Any(child =>
            !ReferenceEquals(child, editing)
            && string.Equals(child.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (clash)
        {
            issues.Add(new ValidationIssue(
                NameField,
                $"'{parent.Name}' already contains something called '{name.Trim()}'."));
        }
    }
}
