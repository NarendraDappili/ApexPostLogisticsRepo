using System.Text.RegularExpressions;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Screens;

/// <summary>
/// Login: the courier says who they are, then either lets the app get a development token from the API
/// (POST dev/token, the development stand-in for a real sign-in; SECURITY.md) or pastes a token, for example one from
/// dotnet user-jwts. The token goes to the token store only: it is never shown, logged or written anywhere. Without
/// a server the courier is signed in offline: scans are recorded and saved as usual, and uploads start after a
/// sign-in that gets a token.
/// </summary>
internal sealed partial class LoginScreen(ConsoleScreen screen, CourierSession session, ApexApiClient api, ITokenStore tokens, TimeProvider clock)
{
    /// <summary>Asks until the courier is signed in. Returns <see langword="false"/> when the input ends.</summary>
    public async Task<bool> RunAsync()
    {
        screen.WriteLine();
        screen.WriteLine("Login");
        var courierId = AskCourierId();
        if (courierId is null) return false;

        await tokens.ClearAsync();
        while (true)
        {
            var pasted = screen.AskSecret("Token: press Enter to get one from the server, or paste one (dotnet user-jwts)");
            if (pasted is null) return false;

            if (string.IsNullOrWhiteSpace(pasted))
            {
                session.SignIn(courierId);
                await GetDevelopmentTokenAsync(courierId);
                return true;
            }

            var (token, problem) = PastedToken.Read(pasted, courierId, clock.GetUtcNow());
            if (token is null)
            {
                screen.WriteLine(problem!);
                continue;
            }

            await tokens.SaveAsync(token.Value, token.ExpiresAtUtc);
            session.SignIn(courierId);
            screen.WriteLine($"Signed in as {courierId} with your token. Scans upload to the server (sign-in valid until {ConsoleScreen.Time(token.ExpiresAtUtc)} UTC).");
            return true;
        }
    }

    private string? AskCourierId()
    {
        var previous = session.LastCourierId;
        while (true)
        {
            var answer = screen.Ask(previous is null ? "Courier ID (for example CR-001)" : $"Courier ID (Enter for {previous})")?.Trim().ToUpperInvariant();
            if (answer is null) return null;
            if (answer.Length == 0 && previous is not null) return previous;
            if (CourierIdFormat().IsMatch(answer)) return answer;

            screen.WriteLine($"A courier ID has letters, digits and hyphens only, at most {ShipmentScan.MaximumCourierIdLength} characters.");
        }
    }

    private async Task GetDevelopmentTokenAsync(string courierId)
    {
        try
        {
            var token = await api.GetDevelopmentTokenAsync(courierId);
            await tokens.SaveAsync(token.AccessToken, token.ExpiresAtUtc);
            screen.WriteLine($"Signed in as {courierId}. Scans upload to the server (sign-in valid until {ConsoleScreen.Time(token.ExpiresAtUtc)} UTC).");
        }
        catch (Exception exception) when (exception is ApexApiException or ApiUnavailableException)
        {
            screen.WriteLine($"Signed in as {courierId}, offline: {exception.Message}");
            screen.WriteLine("Scans are saved on this device. Sign out and in again with a network to upload them.");
        }
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CourierIdFormat();
}
