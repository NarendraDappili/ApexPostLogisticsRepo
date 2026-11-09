using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ApexPost.Api.Security;

/// <summary>
/// The PartnerApi configuration section: one entry per key. In Development the keys are user-secrets; elsewhere they
/// come from the deployment's configuration (a key vault, environment variables). None is ever committed.
/// A partner can have two keys at once, so a key is rotated by adding the new one, moving the partner over, and then
/// removing the old one.
/// </summary>
internal sealed class PartnerApiKeyOptions
{
    public const string Section = "PartnerApi";

    public List<PartnerApiKeyEntry> Keys { get; set; } = [];
}

internal static class PartnerApiKeyRegistration
{
    public static IServiceCollection AddPartnerApiKeys(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PartnerApiKeyOptions>(configuration.GetSection(PartnerApiKeyOptions.Section));
        services.AddSingleton<PartnerApiKeys>();
        return services;
    }
}

/// <summary>A partner's name, which is logged, and its key, which never is.</summary>
internal sealed class PartnerApiKeyEntry
{
    public string? Partner { get; set; }

    public string? Key { get; set; }
}

/// <summary>
/// The partner keys, checked at start-up and kept only as SHA-256 hashes. A presented key is hashed and compared with
/// every stored hash in constant time, so neither the comparison's timing nor an early exit reveals how much of a
/// key was right.
/// </summary>
internal sealed class PartnerApiKeys
{
    /// <summary>
    /// 32 characters: a key is a random value the API issues (for example 32 random bytes in Base64, 44 characters),
    /// never a word a person chose. Anything shorter is refused at start-up.
    /// </summary>
    public const int MinimumKeyLength = 32;

    private readonly (string Partner, byte[] KeyHash)[] _keys;

    public PartnerApiKeys(IOptions<PartnerApiKeyOptions> options, ILogger<PartnerApiKeys> logger)
    {
        var entries = options.Value.Keys;
        var problems = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var where = $"{PartnerApiKeyOptions.Section}:Keys:{i}";
            if (string.IsNullOrWhiteSpace(entries[i].Partner)) problems.Add($"{where}:Partner is missing");
            // Only the length is reported: the message must never contain the key.
            if ((entries[i].Key?.Trim().Length ?? 0) < MinimumKeyLength) problems.Add($"{where}:Key is shorter than {MinimumKeyLength} characters");
        }

        var hashes = entries.Select(entry => (Partner: entry.Partner?.Trim() ?? string.Empty, KeyHash: Hash(entry.Key?.Trim() ?? string.Empty))).ToArray();
        if (hashes.DistinctBy(entry => Convert.ToHexString(entry.KeyHash)).Count() < hashes.Length) problems.Add("two entries have the same key");
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The partner API keys are not valid: {string.Join("; ", problems)}. Each key needs a partner name and a random value of at least "
                + $"{MinimumKeyLength} characters (SECURITY.md).");
        }

        _keys = hashes;
        if (_keys.Length == 0)
        {
            logger.LogWarning("No partner API keys are configured ({Section}:Keys), so every partner request is refused with 401", PartnerApiKeyOptions.Section);
        }
    }

    /// <summary>The partner whose key this is, or <see langword="null"/> if it is no partner's key.</summary>
    public string? FindPartner(string? presentedKey)
    {
        if (string.IsNullOrEmpty(presentedKey)) return null;

        var presented = Hash(presentedKey);
        string? partner = null;
        // Every key is compared, also after a match, so the time taken does not depend on which key matched.
        foreach (var key in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(presented, key.KeyHash)) partner ??= key.Partner;
        }
        return partner;
    }

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
}
