using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.ValueObjects;

public class CodeValueTests
{
    [Theory]
    [InlineData(" BR-001 ", "BR-001")]
    [InlineData("staff-42", "STAFF-42")]
    [InlineData("product-a", "PRODUCT-A")]
    [InlineData("zone-south", "ZONE-SOUTH")]
    public void Constructors_normalize_codes(string input, string expected)
    {
        Assert.Equal(expected, new BranchCode(input).Value);
        Assert.Equal(expected, new StaffId(input).Value);
        Assert.Equal(expected, new ProductId(input).Value);
        Assert.Equal(expected, new ZoneCode(input).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructors_reject_empty_or_whitespace_codes(string value)
    {
        Assert.Throws<ArgumentException>(() => new BranchCode(value));
        Assert.Throws<ArgumentException>(() => new StaffId(value));
        Assert.Throws<ArgumentException>(() => new ProductId(value));
        Assert.Throws<ArgumentException>(() => new ZoneCode(value));
    }

    [Fact]
    public void Constructors_reject_null_codes()
    {
        Assert.Throws<ArgumentNullException>(() => new BranchCode(null!));
        Assert.Throws<ArgumentNullException>(() => new StaffId(null!));
        Assert.Throws<ArgumentNullException>(() => new ProductId(null!));
        Assert.Throws<ArgumentNullException>(() => new ZoneCode(null!));
    }

    [Fact]
    public void Equivalent_normalized_codes_compare_equal()
    {
        Assert.Equal(new BranchCode("br-001"), new BranchCode(" BR-001 "));
        Assert.NotEqual(new BranchCode("BR-001"), new BranchCode("BR-002"));
    }
}
