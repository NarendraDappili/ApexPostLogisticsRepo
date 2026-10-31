using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;

namespace ApexPost.Application.Catalogue;

public sealed record PriceRevisionRequest([property: NotBlank(ProductCategory.MaxCodeLength)] string CategoryCode, decimal Percentage);

/// <summary>
/// Set-based catalogue writes. One statement changes many rows in the database without loading them, so it is
/// atomic on its own and does not go through repositories or the change tracker.
/// </summary>
public interface ICatalogueBulkUpdates
{
    /// <summary>Multiplies the price of every product in the category by <paramref name="factor"/>, rounded to cents.</summary>
    /// <returns>The number of products repriced.</returns>
    Task<int> RevisePricesAsync(string categoryCode, decimal factor, CancellationToken cancellationToken = default);
}

/// <summary>Revises every price in a product category by a percentage, for example +5% on all stamps.</summary>
public sealed class PriceRevisionService
{
    public const decimal MinimumPercentage = -50m;
    public const decimal MaximumPercentage = 100m;
    private readonly ICatalogueBulkUpdates _bulkUpdates;

    public PriceRevisionService(ICatalogueBulkUpdates bulkUpdates) =>
        _bulkUpdates = bulkUpdates ?? throw new ArgumentNullException(nameof(bulkUpdates));

    public Task<int> ReviseAsync(PriceRevisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestValidator.Validate(request);
        if (request.Percentage == 0 || request.Percentage < MinimumPercentage || request.Percentage > MaximumPercentage)
        {
            throw new ArgumentOutOfRangeException(nameof(request), $"A price revision must be a non-zero percentage between {MinimumPercentage} and {MaximumPercentage}.");
        }

        return _bulkUpdates.RevisePricesAsync(ProductCategory.NormalizeCode(request.CategoryCode), 1m + request.Percentage / 100m, cancellationToken);
    }
}
