using ApexPost.Application.Catalogue;
using ApexPost.Application.Validation;

namespace ApexPost.Application.Tests.Catalogue;

public sealed class PriceRevisionServiceTests
{
    [Theory]
    [InlineData(5, 1.05)]
    [InlineData(-10, 0.90)]
    [InlineData(100, 2.00)]
    public async Task A_percentage_becomes_a_price_factor_for_the_normalized_category(decimal percentage, decimal factor)
    {
        var bulk = new RecordingBulkUpdates();

        var repriced = await new PriceRevisionService(bulk).ReviseAsync(new PriceRevisionRequest(" stamps ", percentage));

        Assert.Equal(3, repriced);
        Assert.Equal(("STAMPS", factor), (bulk.CategoryCode, bulk.Factor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50.01)]
    [InlineData(100.01)]
    public async Task Zero_or_out_of_range_percentages_are_rejected_before_any_update(decimal percentage)
    {
        var bulk = new RecordingBulkUpdates();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new PriceRevisionService(bulk).ReviseAsync(new PriceRevisionRequest("STAMPS", percentage)));

        Assert.Null(bulk.CategoryCode);
    }

    [Fact]
    public async Task A_blank_category_is_rejected()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => new PriceRevisionService(new RecordingBulkUpdates()).ReviseAsync(new PriceRevisionRequest(" ", 5m)));
    }

    private sealed class RecordingBulkUpdates : ICatalogueBulkUpdates
    {
        public string? CategoryCode { get; private set; }
        public decimal Factor { get; private set; }

        public Task<int> RevisePricesAsync(string categoryCode, decimal factor, CancellationToken cancellationToken = default)
        {
            CategoryCode = categoryCode;
            Factor = factor;
            return Task.FromResult(3);
        }
    }
}
