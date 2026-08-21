using System.Text;
using Patchbay.Core.Security;

namespace Patchbay.Tests;

/// <summary>
/// Storing a secret and getting it back (M3-02).
///
/// Two things are being defended here and they pull in opposite directions. A
/// password must never be written down in a form anyone can read, and a
/// password that cannot be read back must never be mistaken for a corrupt
/// file, a wrong password, or a field to overwrite. Nearly every test below is
/// one of those two.
///
/// The platform call itself is not here — it is in the shell, because
/// <c>Core</c> is platform-neutral. Everything around it is, which is the
/// reason the split is where it is.
/// </summary>
public class SecretProtectionTests
{
    private const string Password = "correct horse battery staple";

    // ── The envelope ────────────────────────────────────────────────────

    [Fact]
    public void An_envelope_survives_the_trip_through_text()
    {
        byte[] payload = [1, 2, 3, 250, 251, 252];

        string text = SecretEnvelope.Create("dpapi", payload).ToString();

        Assert.True(SecretEnvelope.TryParse(text, out SecretEnvelope? read));
        Assert.Equal(SecretEnvelope.CurrentVersion, read.Version);
        Assert.Equal("dpapi", read.Scheme);
        Assert.Equal(payload, read.Payload.ToArray());
    }

    [Fact]
    public void Every_byte_value_survives_the_trip()
    {
        byte[] payload = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];

        string text = SecretEnvelope.Create("dpapi", payload).ToString();

        Assert.True(SecretEnvelope.TryParse(text, out SecretEnvelope? read));
        Assert.Equal(payload, read.Payload.ToArray());
    }

    [Fact]
    public void An_envelope_says_what_it_is_before_it_says_anything_else()
    {
        string text = SecretEnvelope.Create("dpapi", [7]).ToString();

        Assert.StartsWith("pb1:dpapi:", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hunter2")]
    [InlineData("Passw0rd!")]
    // A password that happens to be valid base64. This is the case that makes
    // the marker necessary rather than tidy: without it, deciding whether a
    // field is a secret means trying to decrypt it.
    [InlineData("SGVsbG8gd29ybGQ=")]
    [InlineData("pb1:dpapi")]
    [InlineData("pb1:dpapi:AAAA:AAAA")]
    [InlineData("pb1::AAAA")]
    [InlineData("pb1:dpapi:")]
    [InlineData("pb1:dpapi:not base64 at all")]
    [InlineData("pb:dpapi:AAAA")]
    [InlineData("pbx:dpapi:AAAA")]
    [InlineData("pb0:dpapi:AAAA")]
    [InlineData("pb99999:dpapi:AAAA")]
    [InlineData("xx1:dpapi:AAAA")]
    [InlineData("pb1:dp api:AAAA")]
    [InlineData("pb1:dpapi!:AAAA")]
    public void What_is_not_an_envelope_is_not_read_as_one(string? text)
    {
        Assert.False(SecretEnvelope.TryParse(text, out SecretEnvelope? read));
        Assert.Null(read);
    }

    [Fact]
    public void A_version_this_build_does_not_know_is_still_read()
    {
        // Parsed, not rejected: "saved by a newer Patchbay" and "corrupt" are
        // different sentences, and only something that can read the version
        // can tell them apart.
        Assert.True(SecretEnvelope.TryParse("pb7:dpapi:AAAA", out SecretEnvelope? read));
        Assert.Equal(7, read.Version);
    }

    [Fact]
    public void A_scheme_is_the_same_scheme_whatever_case_it_arrives_in()
    {
        Assert.Equal("dpapi", SecretEnvelope.Create("DPAPI", [1]).Scheme);
        Assert.True(SecretEnvelope.TryParse("pb1:DPAPI:AAAA", out SecretEnvelope? read));
        Assert.Equal("dpapi", read.Scheme);
    }

    [Fact]
    public void An_envelope_cannot_be_made_without_something_to_put_in_it()
    {
        Assert.Throws<ArgumentException>(() => SecretEnvelope.Create("dpapi", []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dp api")]
    [InlineData("dpapi!")]
    [InlineData("dpapi:2")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void An_envelope_cannot_be_stamped_with_a_name_it_could_not_read_back(string scheme)
    {
        Assert.Throws<ArgumentException>(() => SecretEnvelope.Create(scheme, [1]));
    }

    // ── Round trip through a protector ──────────────────────────────────

    [Fact]
    public void A_secret_comes_back_out_the_way_it_went_in()
    {
        ReversingProtector protector = new();

        SecretUnprotectResult result = protector.Unprotect(protector.Protect(Secret.From(Password)));

        Assert.True(result.IsSuccess);
        Assert.Equal(Password, result.Secret!.RevealAsString());
    }

    [Fact]
    public void A_protected_secret_does_not_contain_the_secret()
    {
        ReversingProtector protector = new();

        string stored = protector.Protect(Secret.From(Password));

        Assert.DoesNotContain(Password, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("staple", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_protected_secret_says_who_protected_it()
    {
        ReversingProtector protector = new();

        Assert.True(SecretEnvelope.TryParse(protector.Protect(Secret.From(Password)), out SecretEnvelope? read));
        Assert.Equal(protector.Scheme, read.Scheme);
    }

    [Fact]
    public void An_empty_secret_is_not_a_secret()
    {
        ReversingProtector protector = new();

        Assert.Throws<ArgumentException>(() => protector.Protect(Secret.Empty));
        Assert.Throws<ArgumentNullException>(() => protector.Protect(null!));
    }

    [Fact]
    public void Non_ascii_passwords_survive()
    {
        ReversingProtector protector = new();

        const string awkward = "pässwörd–ünïcode";

        Assert.Equal(awkward, protector.Unprotect(protector.Protect(Secret.From(awkward))).Secret!.RevealAsString());
    }

    // ── Reading something that cannot be read ───────────────────────────

    [Fact]
    public void A_field_that_is_not_a_secret_is_reported_as_not_a_secret()
    {
        SecretUnprotectResult result = new ReversingProtector().Unprotect("hunter2");

        Assert.Equal(SecretUnprotectStatus.NotASecret, result.Status);
        Assert.Null(result.Notice);
        Assert.False(result.ShouldPreserveStoredValue);
    }

    [Fact]
    public void A_secret_from_another_store_is_left_for_that_store()
    {
        // A Credential Manager blob (M3-04) in a document being read by the
        // DPAPI protector. Reporting this as unreadable would invite the shell
        // to offer to overwrite somebody's working password.
        string other = SecretEnvelope.Create("othervault", [1, 2, 3]).ToString();

        SecretUnprotectResult result = new ReversingProtector().Unprotect(other);

        Assert.Equal(SecretUnprotectStatus.WrongScheme, result.Status);
        Assert.True(result.ShouldPreserveStoredValue);
    }

    [Fact]
    public void A_secret_from_a_newer_patchbay_is_left_alone()
    {
        SecretUnprotectResult result = new ReversingProtector().Unprotect("pb2:reverse:AAAA");

        Assert.Equal(SecretUnprotectStatus.TooNew, result.Status);
        Assert.True(result.ShouldPreserveStoredValue);
    }

    [Fact]
    public void The_version_is_checked_before_the_scheme()
    {
        // Both are wrong. The version wins, because a format this build cannot
        // read is not a format whose scheme field means anything.
        SecretUnprotectResult result = new ReversingProtector().Unprotect("pb2:othervault:AAAA");

        Assert.Equal(SecretUnprotectStatus.TooNew, result.Status);
    }

    [Fact]
    public void A_blob_the_platform_will_not_open_is_unreadable_and_not_corrupt()
    {
        // What a DPAPI blob from another Windows account looks like from here.
        string foreign = SecretEnvelope.Create(ReversingProtector.Name, [0xFF, 1, 2]).ToString();

        SecretUnprotectResult result = new ReversingProtector().Unprotect(foreign);

        Assert.Equal(SecretUnprotectStatus.Unreadable, result.Status);
        Assert.True(result.ShouldPreserveStoredValue);
        Assert.Contains("different Windows account", result.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_protector_that_does_not_work_says_so_rather_than_failing_later()
    {
        ReversingProtector protector = new() { Working = false };

        string stored = SecretEnvelope.Create(ReversingProtector.Name, [1]).ToString();

        Assert.Equal(SecretUnprotectStatus.Unavailable, protector.Unprotect(stored).Status);
        Assert.Throws<SecretProtectionException>(() => protector.Protect(Secret.From(Password)));
    }

    // ── Refusing to protect ─────────────────────────────────────────────

    [Fact]
    public void The_unavailable_protector_refuses_rather_than_storing_a_password_in_the_clear()
    {
        SecretProtectionException ex = Assert.Throws<SecretProtectionException>(
            () => UnavailableSecretProtector.Instance.Protect(Secret.From(Password)));

        Assert.DoesNotContain(Password, ex.Message, StringComparison.Ordinal);
        Assert.False(UnavailableSecretProtector.Instance.IsAvailable);
    }

    [Fact]
    public void The_unavailable_protector_still_tells_a_secret_from_an_ordinary_field()
    {
        string stored = SecretEnvelope.Create("dpapi", [1, 2, 3]).ToString();

        Assert.Equal(
            SecretUnprotectStatus.Unavailable,
            UnavailableSecretProtector.Instance.Unprotect(stored).Status);

        Assert.Equal(
            SecretUnprotectStatus.NotASecret,
            UnavailableSecretProtector.Instance.Unprotect("hunter2").Status);
    }

    // ── The result ──────────────────────────────────────────────────────

    [Fact]
    public void A_result_never_prints_the_secret()
    {
        // A record prints every property it has, and one of this record's
        // properties is a password. One log line, one debugger watch, one
        // string interpolation, and it is written down somewhere permanent.
        string printed = SecretUnprotectResult.Success(Secret.From(Password)).ToString();

        Assert.DoesNotContain(Password, printed, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretUnprotectStatus.Unprotected), printed, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_way_of_failing_has_something_to_say_for_itself()
    {
        SecretUnprotectStatus[] failures =
        [
            .. Enum.GetValues<SecretUnprotectStatus>()
                .Where(s => s != SecretUnprotectStatus.Unprotected
                    && s != SecretUnprotectStatus.NotASecret)
        ];

        Assert.All(failures, status =>
            Assert.False(string.IsNullOrWhiteSpace(SecretUnprotectResult.Failed(status).Notice)));
    }

    [Fact]
    public void A_failure_is_not_a_way_of_reporting_success()
    {
        Assert.Throws<ArgumentException>(
            () => SecretUnprotectResult.Failed(SecretUnprotectStatus.Unprotected));
    }

    /// <summary>
    /// Stands in for a platform store. It reverses the bytes, which is not
    /// protection and is not meant to be — what is under test is everything
    /// around the platform call, and a payload that is obviously not the
    /// plaintext is enough for that.
    ///
    /// A payload starting <c>0xFF</c> is refused, which is how a blob written
    /// by another Windows account behaves when DPAPI is asked to open it here.
    /// </summary>
    private sealed class ReversingProtector : SecretProtector
    {
        public const string Name = "reverse";

        public bool Working { get; init; } = true;

        public override string Scheme => Name;

        public override bool IsAvailable => Working;

        protected override byte[] ProtectCore(ReadOnlySpan<byte> utf8)
        {
            byte[] bytes = utf8.ToArray();
            Array.Reverse(bytes);
            return bytes;
        }

        protected override SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload)
        {
            if (payload[0] == 0xFF)
            {
                return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
            }

            byte[] bytes = payload.ToArray();
            Array.Reverse(bytes);
            return SecretUnprotectResult.Success(Secret.FromUtf8(bytes));
        }
    }
}
