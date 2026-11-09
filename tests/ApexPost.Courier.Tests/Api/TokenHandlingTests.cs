using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Screens;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Courier.Tests.Api;

/// <summary>
/// D10: the courier's token. It lives only in the in-memory token store (the console's SecureStorage); a 401 or an
/// expiry clears it and takes the courier back to Login; a pasted token is checked before it is kept; and no token,
/// key or request body ever reaches a log or the screen.
/// </summary>
public sealed class TokenHandlingTests : IDisposable
{
    private static readonly DateTimeOffset InAnHour = DateTimeOffset.UtcNow.AddHours(1);
    private readonly CourierAppRun _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task A_401_clears_the_token_and_takes_the_courier_back_to_login_and_nothing_is_lost()
    {
        _app.Api.RevokedTokens.Add("fake-token-CR-001#1");
        IReadOnlyList<ShipmentScan> scans = [];
        string? token = null;

        var output = await _app.RunAsync(
            [
                "CR-001", "",                                            // signed in with token #1, which the server revokes
                "1", "APX-30000001", "1", "Riverside Depot", "", "",
                "3",                                                     // sync: 401
                "", "",                                                  // back at Login: Enter keeps CR-001, Enter gets token #2
                "3",                                                     // sync again: accepted
                "6",
            ],
            services =>
            {
                scans = CourierAppRun.Scans(services);
                token = services.GetRequiredService<ITokenStore>().GetAsync().AsTask().Result;
            });

        Assert.Contains("Sync stopped: Please sign in again. Send an access token in the Authorization header", output, StringComparison.Ordinal);
        Assert.Contains("[warn AuthHeaderHandler] The server no longer accepts the courier's token (401); it was cleared and the courier must sign in again", output, StringComparison.Ordinal);
        Assert.Contains("The server no longer accepts your sign-in. Please sign in again; your scans are saved on this device.", output, StringComparison.Ordinal);
        Assert.Contains("Courier ID (Enter for CR-001)", output, StringComparison.Ordinal);
        Assert.Contains("Sync finished: 1 sent, 1 synced, 0 failed, 0 still pending.", output, StringComparison.Ordinal);
        Assert.Equal(SyncStatus.Synced, Assert.Single(scans).SyncStatus);
        Assert.Equal("fake-token-CR-001#2", token);
    }

    [Fact]
    public async Task An_expired_token_is_never_sent_and_ends_the_sign_in()
    {
        using var host = Host();
        var (tokens, session) = SignIn(host, expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));

        await Assert.ThrowsAsync<ApexApiException>(() => Upload(host));

        Assert.Null(Assert.Single(_app.Api.Server.Requests).Authorization);
        Assert.Null(await tokens.GetAsync());
        Assert.StartsWith("Your sign-in expired at ", session.SignInEndedReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_401_for_an_older_token_does_not_clear_the_one_the_courier_got_meanwhile()
    {
        using var host = Host();
        var (tokens, session) = SignIn(host);
        _app.Api.Server.Respond = _ =>
        {
            // While the request with the first token is out, the courier signs in again.
            tokens.SaveAsync("fake-token-CR-001#99", InAnHour).AsTask().GetAwaiter().GetResult();
            return StubServer.Json(HttpStatusCode.Unauthorized, """{ "status": 401 }""", "application/problem+json");
        };

        await Assert.ThrowsAsync<ApexApiException>(() => Upload(host));

        Assert.Equal("fake-token-CR-001#99", await tokens.GetAsync());
        Assert.Null(session.SignInEndedReason);
    }

    [Fact]
    public async Task A_401_during_a_background_sync_ends_the_sign_in_too()
    {
        using var host = Host();
        var (tokens, session) = SignIn(host);
        _app.Api.RevokedTokens.Add("fake-token-CR-001#1");
        host.Services.GetRequiredService<IScanRepository>().Create(Scan());
        await Until(() => host.Services.GetRequiredService<Offline.Storage.ScanChangeMonitor>().PendingCount == 1);

        await host.Services.GetRequiredService<AutoSyncService>().RunOnceAsync("test", CancellationToken.None);

        Assert.Null(await tokens.GetAsync());
        Assert.NotNull(session.SignInEndedReason);
    }

    [Fact]
    public async Task A_sign_in_that_ends_while_the_menu_waits_leads_to_login_with_the_reason()
    {
        var output = new StringWriter();
        IHost? running = null;
        // The fourth answer is read just after a background sync got a 401 (simulated by ending the sign-in).
        var input = new ScriptedReader(["CR-001", "", "3", "", "", "6"], beforeLine: 2, () =>
        {
            running!.Services.GetRequiredService<ITokenStore>().ClearAsync().AsTask().GetAwaiter().GetResult();
            running.Services.GetRequiredService<CourierSession>().EndSignIn("The server no longer accepts your sign-in. Please sign in again; your scans are saved on this device.");
        });
        using (running = _app.CreateHost(input, output))
        {
            await running.Services.GetRequiredService<CourierApp>().RunAsync();
        }

        Assert.DoesNotContain("you are signed in offline", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("The server no longer accepts your sign-in.", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Courier ID (Enter for CR-001)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pasted_token_signs_the_courier_in_and_is_never_shown()
    {
        var jwt = Jwt(new JsonObject { ["sub"] = "Kofi", ["role"] = "Courier", ["courierId"] = "CR-001", ["exp"] = InAnHour.ToUnixTimeSeconds() });
        string? token = null;

        var output = await _app.RunAsync(["CR-001", jwt, "6"], services => token = services.GetRequiredService<ITokenStore>().GetAsync().AsTask().Result);

        Assert.Contains($"Signed in as CR-001 with your token. Scans upload to the server (sign-in valid until {ConsoleScreen.Time(DateTimeOffset.FromUnixTimeSeconds(InAnHour.ToUnixTimeSeconds()))} UTC).", output, StringComparison.Ordinal);
        Assert.Contains($"Uploads: on until {InAnHour.UtcDateTime:HH:mm} UTC", output, StringComparison.Ordinal);
        Assert.Equal(jwt, token);
        Assert.DoesNotContain(jwt.Split('.')[1], output, StringComparison.Ordinal);
        // Pasting replaces the development token: the server was not asked for one.
        Assert.Empty(_app.Api.Server.Requests);
    }

    [Fact]
    public async Task A_pasted_token_that_would_be_refused_is_explained_and_asked_for_again()
    {
        var otherCourier = Jwt(new JsonObject { ["role"] = "Courier", ["courierId"] = "CR-002", ["exp"] = InAnHour.ToUnixTimeSeconds() });

        var output = await _app.RunAsync(["CR-001", otherCourier, "", "6"]);

        Assert.Contains("That token belongs to courier CR-002, not CR-001.", output, StringComparison.Ordinal);
        Assert.Contains("Signed in as CR-001. Scans upload to the server", output, StringComparison.Ordinal);
        Assert.DoesNotContain(otherCourier.Split('.')[1], output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("valid", null)]
    [InlineData("with Bearer", null)]
    [InlineData("roles as a list", null)]
    [InlineData("not a JWT", "That is not a token. Paste the whole token: three parts separated by dots.")]
    [InlineData("payload not JSON", "That is not a token. Paste the whole token: three parts separated by dots.")]
    [InlineData("no expiry", "That token has no expiry (exp), so the server would refuse it.")]
    [InlineData("expired", "That token expired at 2026-01-01 00:00 UTC. Create a new one.")]
    [InlineData("no courierId", "That token has no courierId claim. Create one with: dotnet user-jwts create --project src/ApexPost.Api --role Courier --claim courierId=CR-001")]
    [InlineData("another courier", "That token belongs to courier CR-002, not CR-001.")]
    [InlineData("admin only", "That token does not have the Courier role, so the server would not take its scans.")]
    public void A_pasted_token_is_checked_for_what_the_server_will_check(string kind, string? problem)
    {
        var exp = InAnHour.ToUnixTimeSeconds();
        var text = kind switch
        {
            "valid" => Jwt(new JsonObject { ["role"] = "Courier", ["courierId"] = "CR-001", ["exp"] = exp }),
            "with Bearer" => "Bearer " + Jwt(new JsonObject { ["role"] = "Courier", ["courierId"] = "CR-001", ["exp"] = exp }),
            "roles as a list" => Jwt(new JsonObject { ["roles"] = new JsonArray("Admin", "Courier"), ["courierId"] = "CR-001", ["exp"] = exp }),
            "not a JWT" => "not-a-token",
            "payload not JSON" => "eyJhbGciOiJIUzI1NiJ9." + Base64Url("hello") + ".sig",
            "no expiry" => Jwt(new JsonObject { ["role"] = "Courier", ["courierId"] = "CR-001" }),
            "expired" => Jwt(new JsonObject { ["role"] = "Courier", ["courierId"] = "CR-001", ["exp"] = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() }),
            "no courierId" => Jwt(new JsonObject { ["role"] = "Courier", ["exp"] = exp }),
            "another courier" => Jwt(new JsonObject { ["role"] = "Courier", ["courierId"] = "CR-002", ["exp"] = exp }),
            "admin only" => Jwt(new JsonObject { ["role"] = "Admin", ["courierId"] = "CR-001", ["exp"] = exp }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var (token, found) = PastedToken.Read(text, "CR-001", DateTimeOffset.UtcNow);

        Assert.Equal(problem, found);
        Assert.Equal(problem is null, token is not null);
        if (token is not null) Assert.DoesNotContain(token.Value, token.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_token_or_request_body_reaches_the_screen_even_with_every_log_category_at_trace()
    {
        var output = new StringWriter();
        string[] everything = ["--Logging:LogLevel:Default=Trace", "--Logging:LogLevel:Microsoft=Trace", "--Logging:LogLevel:System=Trace", "--Logging:LogLevel:System.Net.Http.HttpClient=Trace", "--Logging:LogLevel:Polly=Trace"];
        _app.Api.RevokedTokens.Add("fake-token-CR-001#1");
        using (var host = _app.CreateHost(new StringReader("CR-001\n\n1\nAPX-30000001\n1\nRiverside Depot\nSecret note\n\n3\n\n\n3\n6"), output, everything))
        {
            await host.Services.GetRequiredService<CourierApp>().RunAsync();
        }

        var text = output.ToString();
        // The screen logger writes Trace and Debug lines as [dbug: the most detailed logging really was on.
        Assert.Contains("[dbug ", text, StringComparison.Ordinal);
        Assert.Contains("Sending HTTP request POST", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret note", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"scanId\"", text, StringComparison.Ordinal);
    }

    private IHost Host() => _app.CreateHost(TextReader.Null, TextWriter.Null);

    /// <summary>Scripted answers that run <paramref name="action"/> just before the answer at <paramref name="beforeLine"/> is read.</summary>
    private sealed class ScriptedReader(string[] lines, int beforeLine, Action action) : TextReader
    {
        private int _next;

        public override string? ReadLine()
        {
            if (_next == beforeLine) action();
            return _next < lines.Length ? lines[_next++] : null;
        }
    }

    private static (ITokenStore Tokens, CourierSession Session) SignIn(IHost host, DateTimeOffset? expiresAtUtc = null)
    {
        var tokens = host.Services.GetRequiredService<ITokenStore>();
        var session = host.Services.GetRequiredService<CourierSession>();
        session.SignIn("CR-001");
        tokens.SaveAsync("fake-token-CR-001#1", expiresAtUtc ?? InAnHour).AsTask().GetAwaiter().GetResult();
        return (tokens, session);
    }

    private static Task<ScanUploadResult> Upload(IHost host) => host.Services.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-30000001", [Scan()]);

    private static ShipmentScan Scan() => ShipmentScan.Record("APX-30000001", ScanType.Pickup, "CR-001", "Riverside Depot", DateTimeOffset.UtcNow.AddMinutes(-10));

    private static string Jwt(JsonObject payload) => $"{Base64Url("""{"alg":"HS256","typ":"JWT"}""")}.{Base64Url(payload.ToJsonString())}.c2lnbmF0dXJl";

    private static string Base64Url(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("The condition was not met in time.");
            await Task.Delay(25);
        }
    }
}
