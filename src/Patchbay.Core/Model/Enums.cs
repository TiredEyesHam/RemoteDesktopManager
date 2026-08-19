namespace Patchbay.Core.Model;

/// <summary>How credentials are supplied for a connection.</summary>
public enum CredentialMode
{
    /// <summary>Ask each time the session connects.</summary>
    Prompt = 0,

    /// <summary>Use the stored profile named by
    /// <see cref="ConnectionSettings.CredentialProfileId"/>.</summary>
    Profile = 1,

    /// <summary>Let Windows supply the signed-in user's credentials.</summary>
    CurrentUser = 2,
}

/// <summary>Maps to the RDP client's gateway usage method.</summary>
public enum GatewayUsage
{
    None = 0,

    /// <summary>Always route through the gateway, including on the LAN.</summary>
    Always = 1,

    /// <summary>Route through the gateway only when a direct attempt fails.</summary>
    WhenDirectFails = 2,
}

/// <summary>Session colour depth in bits per pixel.</summary>
public enum ColourDepth
{
    HighColour15 = 15,
    HighColour16 = 16,
    TrueColour24 = 24,
    TrueColour32 = 32,
}

/// <summary>Where remote audio is played.</summary>
public enum AudioMode
{
    PlayLocally = 0,
    PlayRemotely = 1,
    DoNotPlay = 2,
}

/// <summary>
/// Where an effective setting came from, relative to the node it was resolved
/// for. Drives the inheritance chips in the inspector (M2-18).
/// </summary>
public enum SettingOrigin
{
    /// <summary>Nothing in the ancestry set it; a built-in default applied.</summary>
    Default = 0,

    /// <summary>The node itself sets it — an override.</summary>
    DefinedHere = 1,

    /// <summary>An ancestor sets it.</summary>
    Inherited = 2,
}

/// <summary>
/// How much bandwidth remote audio is allowed to spend (M4-13).
///
/// Dynamic is the setting to leave alone: the server watches the link and
/// picks, which is a better judgement than a number chosen once on a laptop
/// that has since moved between an office and a train.
/// </summary>
public enum AudioQuality
{
    /// <summary>The server decides, and keeps deciding.</summary>
    Dynamic = 0,

    Medium = 1,

    High = 2,
}

/// <summary>
/// What sort of link the session is going over (M4-14). A hint to the server
/// about how much it can spend on how the desktop looks.
///
/// <para>
/// <see cref="Detect"/> is not a link type — it is the absence of one, and it
/// is handled by asking the control to measure rather than by naming a speed.
/// The two are written to different properties for exactly that reason.
/// </para>
/// </summary>
public enum ConnectionQuality
{
    /// <summary>Measure the link and keep measuring it.</summary>
    Detect = 0,

    Modem = 1,

    LowSpeedBroadband = 2,

    /// <summary>Fast, and slow to answer. The combination the others do not cover.</summary>
    Satellite = 3,

    HighSpeedBroadband = 4,

    /// <summary>Fast, with a wide-area round trip.</summary>
    Wan = 5,

    Lan = 6,
}

/// <summary>
/// What to do about a server that cannot prove who it is (M4-09).
///
/// <para>
/// The one setting in this file where the wrong answer is silent. A session
/// that connects to an unauthenticated server looks exactly like a session
/// that connects to an authenticated one, and the difference only becomes
/// visible after somebody has typed a password into it.
/// </para>
/// </summary>
public enum ServerAuthentication
{
    /// <summary>Connect anyway, and say nothing.</summary>
    Connect = 0,

    /// <summary>Do not connect unless the server is proved.</summary>
    Require = 1,

    /// <summary>Ask, once, per server. The setting most people want.</summary>
    Warn = 2,
}

/// <summary>
/// What the gateway will accept as proof of who is connecting (M4-11).
/// Separate from the credentials for the machine behind it, because the two
/// are routinely different accounts and often different kinds of account.
/// </summary>
public enum GatewayCredentialSource
{
    /// <summary>A user name and a password.</summary>
    Password = 0,

    SmartCard = 1,

    /// <summary>Whatever the gateway will take. Lets the gateway choose.</summary>
    Any = 2,
}
