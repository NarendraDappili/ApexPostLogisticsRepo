using System.Text.RegularExpressions;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Screens;

/// <summary>
/// Login: the courier says who they are, and the app asks the API for a token for that courier (POST dev/token, the
/// development stand-in for a real sign-in; SECURITY.md). Without a network the courier is signed in offline: scans
/// are recorded and saved as usual, and uploads start after a sign-in that gets a token.
/// </summary>
internal sealed partial class LoginScreen(ConsoleScreen screen, CourierSession session, ApexApiClient api, ITokenStore tokens)
{
    /// <summary>Asks until a valid courier ID is given. Returns <see langword="false"/> when the input ends.</summary>
    public async Task<bool> RunAsync()
    {
        screen.WriteLine();
        screen.WriteLine("Login");
        while (true)
        {
            var courierId = screen.Ask("Courier ID (for example CR-001)")?.Trim().ToUpperInvariant();
            if (courierId is null) return false;
            if (!CourierIdFormat().IsMatch(courierId))
            {
                screen.WriteLine($"A courier ID has letters, digits and hyphens only, at most {ShipmentScan.MaximumCourierIdLength} characters.");
                continue;
            }

            session.SignIn(courierId);
            await tokens.ClearAsync();
            try
            {
                var token = await api.GetDevelopmentTokenAsync(courierId);
                await tokens.SaveAsync(token.AccessToken);
                screen.WriteLine($"Signed in as {courierId}. Scans upload to the server (sign-in valid until {ConsoleScreen.Time(token.ExpiresAtUtc)} UTC).");
            }
            catch (Exception exception) when (exception is ApexApiException or ApiUnavailableException)
            {
                screen.WriteLine($"Signed in as {courierId}, offline: {exception.Message}");
                screen.WriteLine("Scans are saved on this device. Sign out and in again with a network to upload them.");
            }

            return true;
        }
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CourierIdFormat();
}
