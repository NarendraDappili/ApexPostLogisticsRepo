using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.ValueObjects;

public sealed class TrackingValueObjectTests
{
    [Theory]
    [InlineData("APX-24000001", "APX-24000001")]
    [InlineData(" apx-24000001 ", "APX-24000001")]
    public void A_tracking_number_is_trimmed_and_upper_cased(string input, string expected)
    {
        Assert.Equal(expected, new TrackingNumber(input).Value);
        Assert.Equal(new TrackingNumber(expected), new TrackingNumber(input));
    }

    [Theory]
    [InlineData("APX-2400000")]
    [InlineData("APX-240000011")]
    [InlineData("APX24000001")]
    [InlineData("ABC-24000001")]
    [InlineData("APX-2400000A")]
    [InlineData("APX-٢٤٠٠٠٠٠١")]
    [InlineData("APX-24000001'--")]
    public void A_tracking_number_must_be_apx_and_eight_ascii_digits(string input)
    {
        Assert.Throws<ArgumentException>(() => new TrackingNumber(input));
        Assert.False(TrackingNumber.TryParse(input, out var parsed));
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_tracking_number_is_required(string? input)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TrackingNumber(input!));
        Assert.False(TrackingNumber.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_returns_the_normalised_number()
    {
        Assert.True(TrackingNumber.TryParse("apx-12345678", out var parsed));
        Assert.Equal("APX-12345678", parsed!.Value);
    }

    [Fact]
    public void A_scan_id_is_the_device_prefix_and_a_guid_in_lower_case()
    {
        var guid = Guid.CreateVersion7();

        var scanId = new TrackingScanId("scan::" + guid.ToString("D").ToUpperInvariant());

        Assert.Equal("scan::" + guid.ToString("D"), scanId.Value);
        Assert.Equal(TrackingScanId.Length, scanId.Value.Length);
    }

    [Theory]
    [InlineData("scan::not-a-guid")]
    [InlineData("SCAN::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10")]
    [InlineData("0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10")]
    [InlineData("scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10 ")]
    [InlineData("scan::{0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d1}")]
    [InlineData("scan::0199c1d27a4e7b119c3d2f6a8b1e4d10")]
    public void A_scan_id_rejects_anything_else(string input)
    {
        Assert.Throws<ArgumentException>(() => new TrackingScanId(input));
        Assert.False(TrackingScanId.TryParse(input, out _));
    }

    [Fact]
    public void A_recipient_trims_every_part()
    {
        var recipient = new Recipient("  Ama Mensah ", " +44 20 7946 0000 ", " 12 Harbour Road ", " Portside ");

        Assert.Equal(("Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside"), (recipient.Name, recipient.Phone, recipient.Address, recipient.City));
    }

    [Theory]
    [InlineData("+44 20 7946 0000")]
    [InlineData("(020) 7946-0000")]
    [InlineData("0201234")]
    [InlineData("+123456789012345")]
    public void Valid_phone_numbers(string phone)
    {
        Assert.True(Recipient.IsValidPhone(phone));
        Assert.Equal(phone, new Recipient("A", phone, "B", "C").Phone);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("+1234567890123456")]
    [InlineData("020-ABC-0000")]
    [InlineData("44+ 20 7946 0000")]
    [InlineData("++44 20 7946 0000")]
    [InlineData("020 7946 0000 ext 5")]
    [InlineData("------------------")]
    public void Invalid_phone_numbers(string phone)
    {
        Assert.False(Recipient.IsValidPhone(phone));
        Assert.Throws<ArgumentException>(() => new Recipient("A", phone, "B", "C"));
    }

    [Fact]
    public void Every_recipient_part_is_required_and_limited()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Recipient(" ", "0201234567", "B", "C"));
        Assert.ThrowsAny<ArgumentException>(() => new Recipient("A", "0201234567", "", "C"));
        Assert.ThrowsAny<ArgumentException>(() => new Recipient("A", "0201234567", "B", null!));
        Assert.Throws<ArgumentException>(() => new Recipient(new string('n', Recipient.NameMaxLength + 1), "0201234567", "B", "C"));
        Assert.Throws<ArgumentException>(() => new Recipient("A", "0201234567", new string('a', Recipient.AddressMaxLength + 1), "C"));
        Assert.Throws<ArgumentException>(() => new Recipient("A", "0201234567", "B", new string('c', Recipient.CityMaxLength + 1)));
    }
}
