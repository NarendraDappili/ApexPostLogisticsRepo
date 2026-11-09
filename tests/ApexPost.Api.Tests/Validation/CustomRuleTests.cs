using System.ComponentModel.DataAnnotations;
using ApexPost.Api.Contracts;
using ApexPost.Api.Validation;
using ApexPost.Domain.Enums;

namespace ApexPost.Api.Tests.Validation;

/// <summary>
/// The custom rules on their own: [NotInFuture] (a scan time cannot be in the future), [PhoneNumber], and the
/// upload's IValidatableObject rule that one request cannot carry the same scan twice. The scan upload endpoint
/// arrives in step B6; these tests use the same DataAnnotations validator that [ApiController] runs.
/// </summary>
public sealed class CustomRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-60 * 24)]
    [InlineData(0)]
    [InlineData(5)]
    public void A_scan_time_up_to_five_minutes_ahead_of_the_server_is_accepted(int minutesFromNow)
    {
        Assert.Empty(Validate(Scan(Now.AddMinutes(minutesFromNow))));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(60 * 24 * 365)]
    public void A_scan_time_further_ahead_is_refused_naming_the_field(int minutesFromNow)
    {
        var result = Assert.Single(Validate(Scan(Now.AddMinutes(minutesFromNow))));

        // DataAnnotations alone names the C# property; in a request the API's metadata provider says scannedAtUtc.
        Assert.Equal("ScannedAtUtc cannot be in the future; it is more than 5 minutes ahead of the server clock.", result.ErrorMessage);
        Assert.Equal([nameof(ScanUploadItem.ScannedAtUtc)], result.MemberNames);
    }

    [Fact]
    public void The_future_check_compares_instants_not_clock_readings()
    {
        // 13:03 at UTC+1 is 12:03 UTC: three minutes ahead, inside the allowed drift.
        Assert.Empty(Validate(Scan(new DateTimeOffset(2026, 11, 9, 13, 3, 0, TimeSpan.FromHours(1)))));
        // 12:03 at UTC-1 is 13:03 UTC: an hour ahead.
        Assert.Single(Validate(Scan(new DateTimeOffset(2026, 11, 9, 12, 3, 0, TimeSpan.FromHours(-1)))));
    }

    [Fact]
    public void The_allowed_drift_can_be_set_and_a_missing_time_is_left_to_required()
    {
        var strict = new NotInFutureAttribute { AllowedClockSkewMinutes = 0 };
        var context = new ValidationContext(new object(), new Clock(Now), null) { DisplayName = "scannedAtUtc", MemberName = "ScannedAtUtc" };

        Assert.NotNull(strict.GetValidationResult(Now.AddSeconds(1), context));
        Assert.Null(strict.GetValidationResult(Now, context));
        Assert.Null(strict.GetValidationResult(null, context));
    }

    [Fact]
    public void A_scan_needs_every_field_but_notes()
    {
        var results = Validate(new ScanUploadItem());

        Assert.Equal(["CourierId", "Location", "ScanId", "ScanType", "ScannedAtUtc"], results.SelectMany(result => result.MemberNames).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("scan::not-a-guid")]
    [InlineData("0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10")]
    [InlineData("scan::0199c1d27a4e7b119c3d2f6a8b1e4d10")]
    [InlineData("scan::{0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10}")]
    public void A_scan_id_must_be_the_device_format(string scanId)
    {
        var scan = Scan(Now);

        var result = Assert.Single(Validate(new ScanUploadItem { ScanId = scanId, ScanType = scan.ScanType, Location = scan.Location, CourierId = scan.CourierId, ScannedAtUtc = scan.ScannedAtUtc }));

        Assert.Equal([nameof(ScanUploadItem.ScanId)], result.MemberNames);
    }

    [Fact]
    public void An_undefined_scan_type_is_refused()
    {
        var scan = Scan(Now);

        Assert.Single(Validate(new ScanUploadItem { ScanId = scan.ScanId, ScanType = (TrackingScanType)9, Location = scan.Location, CourierId = scan.CourierId, ScannedAtUtc = Now }));
    }

    [Fact]
    public void One_upload_cannot_carry_the_same_scan_twice()
    {
        var scan = Scan(Now);
        var request = new ScanUploadRequest { Scans = [scan, Scan(Now), new ScanUploadItem { ScanId = scan.ScanId!.ToUpperInvariant().Replace("SCAN::", "scan::", StringComparison.Ordinal) }] };

        var result = Assert.Single(request.Validate(new ValidationContext(request)));

        Assert.Equal($"Each scan ID can appear only once in an upload; repeated: {scan.ScanId}.", result.ErrorMessage);
        Assert.Equal(["scans"], result.MemberNames);
    }

    [Fact]
    public void An_upload_holds_one_to_one_hundred_scans()
    {
        Assert.Single(Validate(new ScanUploadRequest { Scans = [] }));
        Assert.Single(Validate(new ScanUploadRequest { Scans = [.. Enumerable.Range(0, ScanUploadRequest.MaximumScans + 1).Select(_ => Scan(Now))] }));
        Assert.Single(Validate(new ScanUploadRequest()));
        Assert.Empty(Validate(new ScanUploadRequest { Scans = [.. Enumerable.Range(0, ScanUploadRequest.MaximumScans).Select(_ => Scan(Now))] }));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("+44 20 7946 0000", true)]
    [InlineData("(020) 7946-0000", true)]
    [InlineData("020 7946 0000 ext 5", false)]
    [InlineData("12", false)]
    public void The_phone_rule_is_the_domain_rule(string? phone, bool valid)
    {
        Assert.Equal(valid, new PhoneNumberAttribute().IsValid(phone));
    }

    private static ScanUploadItem Scan(DateTimeOffset scannedAt) => new()
    {
        ScanId = "scan::" + Guid.CreateVersion7(),
        ScanType = TrackingScanType.Pickup,
        Location = "Depot 4",
        CourierId = "CR-001",
        ScannedAtUtc = scannedAt,
    };

    // The same validator [ApiController] runs, with "now" fixed through the service provider as in a request.
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model, new Clock(Now), null), results, validateAllProperties: true);
        return results;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider, IServiceProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? this : null;
    }
}
