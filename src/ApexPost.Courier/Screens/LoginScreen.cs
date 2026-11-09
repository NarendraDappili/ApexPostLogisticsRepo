using System.Text.RegularExpressions;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Screens;

/// <summary>Login: the courier says who they are; every scan they record carries this courier ID.</summary>
internal sealed partial class LoginScreen(ConsoleScreen screen, CourierSession session)
{
    /// <summary>Asks until a valid courier ID is given. Returns <see langword="false"/> when the input ends.</summary>
    public bool Run()
    {
        screen.WriteLine();
        screen.WriteLine("Login");
        while (true)
        {
            var courierId = screen.Ask("Courier ID (for example CR-001)")?.Trim().ToUpperInvariant();
            if (courierId is null) return false;
            if (CourierIdFormat().IsMatch(courierId))
            {
                session.SignIn(courierId);
                screen.WriteLine($"Signed in as {courierId}.");
                return true;
            }

            screen.WriteLine($"A courier ID has letters, digits and hyphens only, at most {ShipmentScan.MaximumCourierIdLength} characters.");
        }
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex CourierIdFormat();
}
