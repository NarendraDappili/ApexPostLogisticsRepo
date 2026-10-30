using System.Security.Claims;
using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApexPost.Web.Services;

/// <summary>
/// Dropdown options for fields that reference stored records (branches, counters, products, zones), so users pick
/// a valid code instead of typing one. Views inject this directly, which keeps the options available when the
/// validation filter redisplays a form from the posted model alone.
/// </summary>
public sealed class ReferenceDataLookup(IBranchRepository branches, ICounterRepository counters, IProductRepository products, IShippingRateProvider rates)
{
    /// <summary>Branches the user may view: every branch for Administrators and Auditors, otherwise the home branch.</summary>
    public async Task<IReadOnlyList<SelectListItem>> ViewableBranchesAsync(ClaimsPrincipal user, string? selected, CancellationToken cancellationToken)
    {
        var all = await AllBranchesAsync(cancellationToken).ConfigureAwait(false);
        var home = user.FindFirst(ApexPostClaimTypes.BranchCode)?.Value;
        return all
            .Where(item => BranchAccess.CanViewAllBranches(user) || string.Equals(item.Value, home, StringComparison.OrdinalIgnoreCase))
            .Select(item => new SelectListItem(item.Text, item.Value, string.Equals(item.Value, selected, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public async Task<IReadOnlyList<SelectListItem>> AllBranchesAsync(CancellationToken cancellationToken) =>
        (await branches.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(branch => branch.Code.Value, StringComparer.Ordinal)
            .Select(branch => new SelectListItem($"{branch.Code.Value} - {branch.Name}{(branch.IsOpen ? string.Empty : " (closed)")}", branch.Code.Value))
            .ToArray();

    public async Task<IReadOnlyList<SelectListItem>> OpenCountersAsync(string? branchCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchCode)) return [];
        return (await counters.FindByBranchAsync(new BranchCode(branchCode), cancellationToken).ConfigureAwait(false))
            .Where(counter => counter.IsOpen)
            .OrderBy(counter => counter.Code.Value, StringComparer.Ordinal)
            .Select(counter => new SelectListItem(counter.Code.Value, counter.Code.Value))
            .ToArray();
    }

    public async Task<IReadOnlyList<SelectListItem>> ProductsAsync(CancellationToken cancellationToken) =>
        (await products.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(product => new SelectListItem($"{product.Id.Value} - {product.Name} ({product.UnitPrice.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})", product.Id.Value))
            .ToArray();

    public async Task<IReadOnlyList<SelectListItem>> ZonesAsync(CancellationToken cancellationToken) =>
        (await rates.GetRatesAsync(cancellationToken).ConfigureAwait(false))
            .Select(rate => rate.DestinationZoneCode.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(zone => new SelectListItem(zone, zone))
            .ToArray();
}
