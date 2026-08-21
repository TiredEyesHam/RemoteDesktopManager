using System.Reflection;
using Patchbay.Core.Diagnostics;
using Patchbay.Core.Security;

namespace Patchbay.Tests;

/// <summary>
/// A password in a buffer that can be erased (M3-03).
///
/// <para>
/// The claim under test is narrow and worth stating exactly. This does not
/// make a password safe from anything that can read the process, which the
/// threat model puts out of scope. What it does is make the copies Patchbay
/// holds erasable, so that a password typed at nine is not still legible at
/// five because a <c>string</c> cannot be written over.
/// </para>
/// </summary>
public class SecretTests
{
    private const string Password = "hunter2-correct-horse";

    /// <summary>
    /// Reads the buffer out from under the type, which is the only way to
    /// assert that erasing erased anything. A security property nobody can
    /// check is a comment.
    /// </summary>
    private static byte[] BufferOf(Secret secret) =>
        (byte[])typeof(Secret)
            .GetField("_utf8", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(secret)!;

    // ── Round trips ─────────────────────────────────────────────────────

    [Fact]
    public void A_password_comes_back_out_the_way_it_went_in()
    {
        using Secret secret = Secret.From(Password);

        Assert.Equal(Password, secret.RevealAsString());
    }

    [Theory]
    [InlineData("pässwörd")]
    [InlineData("па роль")]
    [InlineData("🔐🔐")]
    [InlineData(" leading and trailing ")]
    public void An_awkward_password_survives_as_well(string awkward)
    {
        using Secret secret = Secret.From(awkward);

        Assert.Equal(awkward, secret.RevealAsString());
    }

    [Fact]
    public void The_bytes_are_utf8_so_a_password_saved_by_an_earlier_version_still_opens()
    {
        // M3-02 stores UTF-8 of the plaintext. Changing the encoding here
        // would make every already-saved password unreadable, which is a
        // worse outcome than any encoding argument is worth.
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(Password);

        using Secret secret = Secret.FromUtf8(utf8);

        Assert.Equal(Password, secret.RevealAsString());
        Assert.Equal(utf8.Length, secret.Length);
    }

    [Fact]
    public void Reading_from_a_store_makes_no_string_on_the_way()
    {
        // The point of FromUtf8: bytes out of the protector go into the buffer
        // directly. A string made here would stay in the heap for the rest of
        // the run and there would be nothing to erase.
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(Password);

        using Secret secret = Secret.FromUtf8(utf8);

        Assert.True(BufferOf(secret).AsSpan().SequenceEqual(utf8));
    }

    // ── Erasing ─────────────────────────────────────────────────────────

    [Fact]
    public void Erasing_writes_over_the_buffer()
    {
        Secret secret = Secret.From(Password);
        byte[] buffer = BufferOf(secret);

        Assert.Contains(buffer, b => b != 0);

        secret.Dispose();

        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Reading_an_erased_password_throws_rather_than_returning_nothing()
    {
        // Not an empty string. A session that silently connects with no
        // password is a bug that looks exactly like a wrong password, and it
        // would be chased at the far end for as long as that took.
        Secret secret = Secret.From(Password);
        secret.Dispose();

        Assert.Throws<ObjectDisposedException>(() => secret.RevealAsString());
        Assert.Throws<ObjectDisposedException>(() => secret.Reveal(0, (_, _) => { }));
        Assert.True(secret.IsDisposed);
    }

    [Fact]
    public void Erasing_twice_is_not_an_error()
    {
        Secret secret = Secret.From(Password);

        secret.Dispose();
        secret.Dispose();

        Assert.True(secret.IsDisposed);
    }

    [Fact]
    public void Erasing_the_empty_one_does_nothing_at_all()
    {
        // It is shared and handed out as a default, so a caller disposing what
        // it was given must not break the next caller.
        Secret.Empty.Dispose();

        Assert.False(Secret.Empty.IsDisposed);
        Assert.Equal(string.Empty, Secret.Empty.RevealAsString());
        Assert.Same(Secret.Empty, Secret.From(string.Empty));
    }

    // ── Identity outlives plaintext ─────────────────────────────────────

    [Fact]
    public void The_same_password_twice_is_the_same_secret()
    {
        using Secret one = Secret.From(Password);
        using Secret other = Secret.From(Password);

        Assert.Equal(one, other);
        Assert.Equal(one.GetHashCode(), other.GetHashCode());
    }

    [Fact]
    public void A_different_password_is_a_different_secret()
    {
        using Secret one = Secret.From(Password);
        using Secret other = Secret.From(Password + "!");

        Assert.NotEqual(one, other);
    }

    [Fact]
    public void Erasing_destroys_the_plaintext_and_not_the_identity()
    {
        // This is what lets a refused password be erased while a prompt goes
        // on refusing to resubmit it (M3-06). Comparing does not need either
        // plaintext.
        Secret refused = Secret.From(Password);
        using Secret typedAgain = Secret.From(Password);

        refused.Dispose();

        Assert.Equal(refused, typedAgain);
        Assert.True(refused.Matches(Password));
    }

    // ── Asking without allocating ───────────────────────────────────────

    [Fact]
    public void A_typed_password_can_be_checked_without_making_another_secret()
    {
        using Secret secret = Secret.From(Password);

        Assert.True(secret.Matches(Password));
        Assert.False(secret.Matches(Password + "!"));
        Assert.False(secret.Matches(string.Empty));
    }

    [Fact]
    public void A_password_too_long_for_the_stack_is_checked_the_same_way()
    {
        // Over the stack limit the comparison rents a buffer instead, and the
        // rented one goes back to the pool for somebody else to read, so it
        // has to be erased first.
        string long_ = new('x', 1024);

        using Secret secret = Secret.From(long_);

        Assert.True(secret.Matches(long_));
        Assert.False(secret.Matches(new string('x', 1023)));
    }

    [Fact]
    public void An_empty_password_matches_only_an_empty_one()
    {
        using Secret something = Secret.From(Password);

        Assert.True(Secret.Empty.Matches(string.Empty));
        Assert.False(Secret.Empty.Matches("x"));
        Assert.False(something.Matches(string.Empty));
    }

    // ── Printing ────────────────────────────────────────────────────────

    [Fact]
    public void Nothing_about_it_prints_the_password()
    {
        using Secret secret = Secret.From(Password);

        Assert.DoesNotContain(Password, secret.ToString(), StringComparison.Ordinal);
        Assert.Contains(SecretNames.Mask, secret.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A record that took the generated <c>ToString</c>, on purpose.</summary>
    private sealed record Careless(string Server, Secret Password);

    [Fact]
    public void A_record_holding_one_is_safe_even_with_the_generated_ToString()
    {
        // The reason ArchitectureTests exempts a Secret-holding type from
        // having to override ToString. A record prints every property it has,
        // and this property prints a mask.
        using Secret secret = Secret.From(Password);

        Careless careless = new("vm-07", secret);

        Assert.DoesNotContain(Password, careless.ToString(), StringComparison.Ordinal);
        Assert.Contains("vm-07", careless.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mask_is_the_same_width_whatever_the_password_was()
    {
        // A variable-width mask hands over the length, which is the first
        // thing anybody guessing would want.
        using Secret shortOne = Secret.From("x");
        using Secret longOne = Secret.From(new string('x', 64));

        Assert.Equal(shortOne.ToString(), longOne.ToString());
    }
}
