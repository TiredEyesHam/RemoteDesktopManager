using System.Text.Json.Serialization;

namespace Patchbay.Core.Model;

/// <summary>
/// A single remote host. Note what is <em>not</em> here: port, credentials,
/// gateway and display all live in <see cref="ConnectionNode.Settings"/> so
/// they can be inherited. Only the address is intrinsic to the host itself.
/// </summary>
public sealed class ServerNode : ConnectionNode
{
    /// <summary>Host name or IP address. Not inheritable — it identifies the node.</summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Free-form labels for filtering and saved views (M8-12). Get-only, and
    /// populated rather than replaced on load, for the same reason as
    /// <see cref="GroupNode.Children"/>.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public IList<string> Tags { get; } = [];
}
