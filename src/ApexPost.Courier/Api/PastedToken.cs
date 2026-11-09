using System.Text;
using System.Text.Json;

namespace ApexPost.Courier.Api;

/// <summary>A pasted token that the app accepted, with when it expires.</summary>
internal sealed record AcceptedToken(string Value, DateTimeOffset ExpiresAtUtc)
{
    // Never the token itself.
    public override string ToString() => $"AcceptedToken(****, expires {ExpiresAtUtc:u})";
}

/// <summary>
/// Reads a token the courier pasted at Login, for example one from dotnet user-jwts. Only what the app needs is
/// read from the JWT's payload: when it expires, whose it is (courierId) and whether it has the Courier role. The
/// signature is not checked here, because the app has no key to check it with and needs none: the server checks every
/// token it receives. These checks only spare the courier a sign-in that the server would refuse anyway. The messages
/// never contain the token.
/// </summary>
internal static class PastedToken
{
    public static (AcceptedToken? Token, string? Problem) Read(string text, string courierId, DateTimeOffset now)
    {
        var token = text.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token["Bearer ".Length..].Trim();

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || !TryReadPayload(parts[1], out var payload))
        {
            return (null, "That is not a token. Paste the whole token: three parts separated by dots.");
        }

        using (payload)
        {
            var claims = payload.RootElement;
            if (!claims.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var seconds))
            {
                return (null, "That token has no expiry (exp), so the server would refuse it.");
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (expiresAt <= now) return (null, $"That token expired at {expiresAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC. Create a new one.");

            if (!claims.TryGetProperty(ApiClaims.CourierId, out var owner) || owner.ValueKind != JsonValueKind.String)
            {
                return (null, $"That token has no {ApiClaims.CourierId} claim. Create one with: dotnet user-jwts create --project src/ApexPost.Api --role Courier --claim {ApiClaims.CourierId}={courierId}");
            }

            if (!string.Equals(owner.GetString(), courierId, StringComparison.Ordinal))
            {
                return (null, $"That token belongs to courier {owner.GetString()}, not {courierId}.");
            }

            if (!HasRole(claims, ApiClaims.CourierRole)) return (null, "That token does not have the Courier role, so the server would not take its scans.");

            return (new AcceptedToken(token, expiresAt), null);
        }
    }

    private static bool TryReadPayload(string segment, out JsonDocument payload)
    {
        payload = null!;
        try
        {
            var base64 = segment.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
            payload = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            if (payload.RootElement.ValueKind == JsonValueKind.Object) return true;
            payload.Dispose();
            return false;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    // dotnet user-jwts and /dev/token write "role"; some identity providers write "roles". Either is a string or a list.
    private static bool HasRole(JsonElement claims, string role) =>
        new[] { "role", "roles" }.Any(name => claims.TryGetProperty(name, out var value) && value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() == role,
            JsonValueKind.Array => value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == role),
            _ => false,
        });
}

/// <summary>The claim and role names the API's tokens use (SECURITY.md).</summary>
internal static class ApiClaims
{
    public const string CourierId = "courierId";
    public const string CourierRole = "Courier";
}
