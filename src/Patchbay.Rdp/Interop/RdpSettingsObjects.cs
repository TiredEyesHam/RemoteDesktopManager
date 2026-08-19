using Patchbay.Core.Sessions;

namespace Patchbay.Rdp.Interop;

/// <summary>
/// Finds the settings objects hanging off a control, newest first (M4-04).
///
/// <para>
/// The control is not one property bag. Each generation added a new interface
/// and exposed it under a new property name, and the old names are all still
/// there returning the old interfaces — so the way to get the most capable
/// object is to ask for the highest name that answers, not to work out which
/// control this is and look it up.
/// </para>
///
/// <para>
/// <b>The numbering is off by one from the interfaces, and always has been.</b>
/// <c>AdvancedSettings3</c> returns <c>IMsRdpClientAdvancedSettings2</c>.
/// Reading the property name as though it were the interface version is how
/// somebody concludes that a control lacks a setting it has had for four
/// generations. Nothing here depends on the mapping, which is the point of
/// walking the names instead.
/// </para>
///
/// <para>
/// One instance per control, and the answers are cached: it is the same object
/// every time, and a plan of twenty writes should not cost twenty lookups.
/// </para>
/// </summary>
internal sealed class RdpSettingsObjects
{
    /// <summary>
    /// The names to try for each target, best first. A name a control has
    /// never heard of costs one failed lookup and nothing else, so the lists
    /// reach further forward than any control that exists today.
    /// </summary>
    private static readonly Dictionary<RdpSettingTarget, string[]> Names = new()
    {
        [RdpSettingTarget.AdvancedSettings] =
        [
            "AdvancedSettings9",
            "AdvancedSettings8",
            "AdvancedSettings7",
            "AdvancedSettings6",
            "AdvancedSettings5",
            "AdvancedSettings4",
            "AdvancedSettings3",
            "AdvancedSettings2",
            "AdvancedSettings",
        ],
        [RdpSettingTarget.SecuredSettings] =
        [
            "SecuredSettings3",
            "SecuredSettings2",
            "SecuredSettings",
        ],
        [RdpSettingTarget.TransportSettings] =
        [
            "TransportSettings4",
            "TransportSettings3",
            "TransportSettings2",
            "TransportSettings",
        ],
    };

    private readonly RdpClientInstance _client;
    private readonly Dictionary<RdpSettingTarget, object?> _resolved = [];

    internal RdpSettingsObjects(RdpClientInstance client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// The object to write <paramref name="target"/>'s properties on, or null
    /// when this control has nothing of the kind. Null is an ordinary answer
    /// for an older control and not an error.
    /// </summary>
    internal object? Resolve(RdpSettingTarget target)
    {
        if (target is RdpSettingTarget.Client)
        {
            return _client.ComObject;
        }

        if (_resolved.TryGetValue(target, out object? cached))
        {
            return cached;
        }

        object? found = null;

        if (Names.TryGetValue(target, out string[]? candidates))
        {
            foreach (string name in candidates)
            {
                if (!_client.Has(name))
                {
                    continue;
                }

                try
                {
                    found = _client.GetSettings(name);
                    break;
                }
                catch (RdpEngineException)
                {
                    // It answered a lookup and then would not hand the object
                    // over. Keep walking: an older name on the same control
                    // usually will.
                }
            }
        }

        _resolved[target] = found;

        return found;
    }
}
