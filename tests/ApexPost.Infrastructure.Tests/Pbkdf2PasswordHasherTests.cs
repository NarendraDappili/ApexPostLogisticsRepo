using ApexPost.Infrastructure.Security;

namespace ApexPost.Infrastructure.Tests;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new(10_000);

    [Fact]
    public void Hash_is_salted_versioned_and_never_contains_the_password()
    {
        var first = _hasher.Hash("correct horse battery");
        var second = _hasher.Hash("correct horse battery");

        Assert.StartsWith("PBKDF2-SHA256$10000$", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("correct horse battery", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_accepts_only_the_original_password()
    {
        var hash = _hasher.Hash("correct horse battery");

        Assert.True(_hasher.Verify("correct horse battery", hash));
        Assert.False(_hasher.Verify("Correct horse battery", hash));
        Assert.False(_hasher.Verify("", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("PBKDF2-SHA256$abc$c2FsdA==$aGFzaA==")]
    [InlineData("PBKDF2-SHA256$10000$not-base64$aGFzaA==")]
    public void Malformed_or_plain_text_stored_values_never_verify(string? storedValue)
    {
        Assert.False(_hasher.Verify("plain-text-password", storedValue));
    }

    [Fact]
    public void A_hash_made_with_a_different_work_factor_still_verifies()
    {
        var older = new Pbkdf2PasswordHasher(20_000).Hash("rotating-work-factor");

        Assert.True(_hasher.Verify("rotating-work-factor", older));
    }
}
