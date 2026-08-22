using System.Globalization;
using System.Text;
using Patchbay.Core.Import;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

/// <summary>
/// The tests that decide whether the <c>.rdp</c> importer is allowed to ship.
///
/// A <c>.rdg</c> is a document somebody built. A <c>.rdp</c> arrives: emailed
/// by a supplier, downloaded from a portal, handed over on a share. The format
/// can do considerably more than name a machine — it can hand the far end
/// every drive on this computer, the smart card in its reader and the
/// microphone; it can name a program to run instead of a desktop; and it can
/// ask the client not to check who it is connecting to. In October 2024 a
/// campaign used signed <c>.rdp</c> attachments to do the first of those at
/// scale.
///
/// So the rule these tests hold to is one sentence: an imported file may
/// switch a redirection off, but it may not switch on one that Patchbay leaves
/// off. Each test names what it stands for. If one starts failing, the
/// importer is not to be released with it failing.
/// </summary>
public class RdpImporterSecurityTests
{
    /// <summary>Every redirection the format has, all of them asked for.</summary>
    private const string EverythingOn = """
        full address:s:web-01
        drivestoredirect:s:*
        devicestoredirect:s:*
        redirectclipboard:i:1
        redirectprinters:i:1
        redirectsmartcards:i:1
        redirectcomports:i:1
        redirectposdevices:i:1
        audiocapturemode:i:1
        """;

    // ── What a file may not switch on ───────────────────────────────────

    /// <summary>
    /// The one the 2024 campaign used. A single asterisk offers every drive
    /// the account can see, including the one holding the profile that opened
    /// the file, and the far end can read and write all of it.
    /// </summary>
    [Fact]
    public void A_file_cannot_switch_on_drive_redirection()
    {
        Assert.Null(Settings("drivestoredirect:s:*").RedirectDrives);
    }

    [Fact]
    public void A_file_cannot_switch_on_the_smart_card_reader()
    {
        Assert.Null(Settings("redirectsmartcards:i:1").RedirectSmartCards);
    }

    [Fact]
    public void A_file_cannot_switch_on_the_microphone()
    {
        Assert.Null(Settings("audiocapturemode:i:1").RedirectMicrophone);
    }

    [Fact]
    public void A_file_cannot_switch_on_ports_or_devices()
    {
        ConnectionSettings settings = Settings(
            "redirectcomports:i:1", "devicestoredirect:s:*", "redirectposdevices:i:1");

        Assert.Null(settings.RedirectPorts);
        Assert.Null(settings.RedirectDevices);
        Assert.Null(settings.RedirectPointOfSaleDevices);
    }

    /// <summary>
    /// The other half of the rule, and the half that keeps it honest. Off is
    /// never an attack, so a file that asks for less than the default gets it.
    /// </summary>
    [Fact]
    public void A_file_can_switch_a_redirection_off()
    {
        Assert.False(Settings("drivestoredirect:s:").RedirectDrives);
        Assert.False(Settings("redirectclipboard:i:0").RedirectClipboard);
        Assert.False(Settings("audiocapturemode:i:0").RedirectMicrophone);
    }

    /// <summary>
    /// Refusing something the default already grants would be theatre: it
    /// would change nothing about the connection and would put a warning on
    /// the screen saying otherwise.
    /// </summary>
    [Fact]
    public void A_redirection_Patchbay_already_allows_is_imported_as_asked()
    {
        Assert.True(Settings("redirectclipboard:i:1").RedirectClipboard);
    }

    /// <summary>
    /// The rule reads the defaults rather than a list of settings written out
    /// beside it, so it keeps meaning the same thing if a default ever moves.
    /// This test is what says so.
    /// </summary>
    [Fact]
    public void What_may_be_switched_on_is_decided_by_the_defaults()
    {
        ConnectionSettings defaults = ConnectionSettings.Defaults;
        ConnectionSettings imported = Only(RdpImporter.Import(EverythingOn)).Settings;

        (string What, bool? Default, bool? Imported)[] redirections =
        [
            ("the clipboard", defaults.RedirectClipboard, imported.RedirectClipboard),
            ("drives", defaults.RedirectDrives, imported.RedirectDrives),
            ("printers", defaults.RedirectPrinters, imported.RedirectPrinters),
            ("smart cards", defaults.RedirectSmartCards, imported.RedirectSmartCards),
            ("ports", defaults.RedirectPorts, imported.RedirectPorts),
            ("devices", defaults.RedirectDevices, imported.RedirectDevices),
            ("point-of-sale devices", defaults.RedirectPointOfSaleDevices, imported.RedirectPointOfSaleDevices),
            ("the microphone", defaults.RedirectMicrophone, imported.RedirectMicrophone),
        ];

        foreach ((string what, bool? fallback, bool? actual) in redirections)
        {
            Assert.True(
                fallback is true ? actual is true : actual is null,
                $"A file asked to switch on {what}, where the default is {fallback}, and got {actual}.");
        }
    }

    [Fact]
    public void Everything_refused_is_named_in_the_warnings()
    {
        ImportResult result = RdpImporter.Import(EverythingOn);

        string warnings = string.Join(' ', result.Warnings);

        Assert.Contains("your drives", warnings, StringComparison.Ordinal);
        Assert.Contains("your smart card reader", warnings, StringComparison.Ordinal);
        Assert.Contains("your microphone", warnings, StringComparison.Ordinal);
        Assert.Contains("switch a redirection off but not on", warnings, StringComparison.Ordinal);
    }

    // ── What a file may not weaken ──────────────────────────────────────

    /// <summary>
    /// Authentication level 0 is "connect anyway, and say nothing". A session
    /// to a server that could not prove who it is looks pixel for pixel like a
    /// session to one that could, and the difference only shows up after
    /// somebody has typed a password into it.
    /// </summary>
    [Fact]
    public void A_file_cannot_silence_the_check_on_a_server_identity()
    {
        ImportResult result = RdpImporter.Import("full address:s:web-01\nauthentication level:i:0\n");

        Assert.Null(Only(result).Settings.ServerAuthentication);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("not to check the identity", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_can_ask_for_a_stricter_check_than_the_default()
    {
        Assert.Equal(
            ServerAuthentication.Require,
            Settings("authentication level:i:1").ServerAuthentication);
    }

    /// <summary>
    /// Without network level authentication the logon happens on a screen the
    /// far end draws. Patchbay has no setting for it, so there is nothing to
    /// refuse — but a file that asks for it is worth a sentence.
    /// </summary>
    [Fact]
    public void Switching_off_network_level_authentication_is_reported()
    {
        ImportResult result = RdpImporter.Import(
            "full address:s:web-01\nenablecredsspsupport:i:0\n");

        Assert.Contains(
            result.Warnings,
            w => w.Contains("network level authentication", StringComparison.Ordinal));
    }

    // ── What a file wants to run ────────────────────────────────────────

    [Fact]
    public void A_program_the_file_wants_to_run_is_quoted_rather_than_dropped_quietly()
    {
        ImportResult result = RdpImporter.Import(
            "full address:s:web-01\nalternate shell:s:powershell -enc SQBFAFgA\n");

        Assert.Contains(
            result.Warnings,
            w => w.Contains("powershell -enc SQBFAFgA", StringComparison.Ordinal));
    }

    [Fact]
    public void A_RemoteApp_file_is_reported_even_where_it_names_no_program()
    {
        ImportResult result = RdpImporter.Import(
            "full address:s:web-01\nremoteapplicationmode:i:1\n");

        Assert.Contains(
            result.Warnings,
            w => w.Contains("runs a program instead of a desktop", StringComparison.Ordinal));
    }

    /// <summary>
    /// The quote goes on screen, so what it can carry matters. A direction
    /// override in the middle of a program name makes the rest of the sentence
    /// read backwards.
    /// </summary>
    [Fact]
    public void Quoted_text_carries_no_control_or_direction_characters()
    {
        ImportResult result = RdpImporter.Import(
            "full address:s:web-01\nalternate shell:s:calc\u202Eexe.\u0007bad\n");

        string warnings = string.Join(' ', result.Warnings);

        Assert.DoesNotContain('\u202E', warnings);
        Assert.DoesNotContain('\u0007', warnings);
        Assert.Contains("calcexe.bad", warnings, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_that_reads_backwards_is_cleaned_before_it_reaches_the_tree()
    {
        ServerNode server = Only(RdpImporter.Import("full address:s:web-01\n", "WEB\u202E10-DRP"));

        Assert.DoesNotContain('\u202E', server.Name);
        Assert.Equal("WEB10-DRP", server.Name);
    }

    // ── What a file must not give up ────────────────────────────────────

    /// <summary>
    /// The blob is DPAPI-protected to whoever saved the file, so reading it is
    /// usually impossible and always somebody else's decision. It is counted,
    /// and nothing that holds it survives the parse.
    /// </summary>
    [Fact]
    public void A_saved_password_is_never_read_out_of_the_file()
    {
        const string Canary = "504154434842415943414e415259";

        ImportResult result = RdpImporter.Import(
            $"full address:s:web-01\nusername:s:rdpadmin\npassword 51:b:{Canary}\n");

        ServerNode server = Only(result);

        string everything = string.Join(
            '\n',
            result.Warnings
                .Append(server.Name)
                .Append(server.HostName)
                .Append(server.Settings.UserName)
                .Append(server.Settings.Domain));

        Assert.DoesNotContain(Canary, everything, StringComparison.OrdinalIgnoreCase);
        Assert.Null(RdpFile.Read($"password 51:b:{Canary}\n").Text("password 51"));
    }

    // ── What a file must not be able to do to the reader ────────────────

    /// <summary>
    /// A bound before the allocation, not after it. Without one, a file made
    /// of a single very long line is read into memory in full before anything
    /// looks at it.
    /// </summary>
    [Fact]
    public void A_file_far_larger_than_any_real_one_is_refused()
    {
        byte[] huge = Encoding.UTF8.GetBytes(new string('x', RdpFile.MaxCharacters + 1));

        using MemoryStream stream = new(huge);

        Assert.Throws<ImportException>(() => RdpFile.Read(stream));
    }

    /// <summary>
    /// The address reaches a connection attempt, so it goes through the same
    /// check as one somebody typed. Anything else means a file decides what
    /// gets handed to the control.
    /// </summary>
    [Theory]
    [InlineData("full address:s:web-01 & calc.exe")]
    [InlineData("full address:s:/../../etc/hosts")]
    [InlineData("full address:s:web-01\u202Ekcatta")]
    public void An_address_that_is_not_an_address_is_refused(string line)
    {
        Assert.Throws<ImportException>(() => RdpImporter.Import($"{line}\n"));
    }

    [Fact]
    public void An_address_Patchbay_would_refuse_to_type_is_not_quoted_back_raw()
    {
        ImportException ex = Assert.Throws<ImportException>(
            () => RdpImporter.Import("full address:s:web-01\u202Ekcatta\n"));

        Assert.DoesNotContain('\u202E', ex.Message);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static ServerNode Only(ImportResult result) =>
        Assert.IsType<ServerNode>(Assert.Single(result.Root.Children));

    private static ConnectionSettings Settings(params string[] lines) =>
        Only(RdpImporter.Import(string.Create(
            CultureInfo.InvariantCulture,
            $"full address:s:web-01\n{string.Join('\n', lines)}\n"))).Settings;
}
