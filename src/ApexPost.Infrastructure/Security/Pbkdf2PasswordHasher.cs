using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ApexPost.Application.Abstractions;

namespace ApexPost.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing with a random 16-byte salt per password.
/// Stored format: <c>PBKDF2-SHA256$iterations$salt-base64$hash-base64</c>, so the work factor can be raised later
/// without invalidating existing hashes.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "PBKDF2-SHA256";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    public const int DefaultIterations = 210_000;
    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 10_000);
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, _iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Algorithm, _iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string? passwordHash)
    {
        if (string.IsNullOrEmpty(password) || !TryParse(passwordHash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryParse(string? passwordHash, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0; salt = []; hash = [];
        var parts = passwordHash?.Split('$');
        if (parts is not [Algorithm, var rawIterations, var rawSalt, var rawHash]) return false;
        try
        {
            salt = Convert.FromBase64String(rawSalt);
            hash = Convert.FromBase64String(rawHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return int.TryParse(rawIterations, NumberStyles.None, CultureInfo.InvariantCulture, out iterations) && iterations > 0 && salt.Length > 0 && hash.Length > 0;
    }
}
