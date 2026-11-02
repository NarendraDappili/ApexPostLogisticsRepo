using System.Security.Claims;
using ApexPost.Application.Pricing;
using ApexPost.Application.Queries;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApexPost.Web.Services;

/// <summary>
/// Dropdown options for fields that reference stored records (branches, counters, products, zones), so users pick
/// a valid code instead of typing one. Views inject this directly, which keeps the options available when the
/// validation filter redisplays a form from the posted model alone. Options come from the read-side query services.
/// </summary>
public sealed class ReferenceDataLookup(IBranchQueries branches, ICatalogueQueries catalogue, IShippingRateProvider rates, IConfiguration configuration)
{
    /// <summary>The low-stock threshold the dashboard and reports use, so the catalogue can flag the same products.</summary>
    public int LowStockThreshold => configuration.GetValue<int?>("ApexPost:Inventory:LowStockThreshold") ?? 10;

    /// <summary>
    /// Enum options with readable labels ("Counter Clerk", "Digital Wallet"). The values are the numeric enum values,
    /// exactly as Html.GetEnumSelectList produces them, so model binding is unchanged.
    /// </summary>
    public static IReadOnlyList<SelectListItem> EnumOptions<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(value => new SelectListItem(DisplayText.Of(value), Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture)))];

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
        (await branches.GetBranchesAsync(cancellationToken).ConfigureAwait(false))
            .Select(branch => new SelectListItem($"{branch.BranchCode} - {branch.Name}{(branch.IsOpen ? string.Empty : " (closed)")}", branch.BranchCode))
            .ToArray();

    public async Task<IReadOnlyList<SelectListItem>> OpenCountersAsync(string? branchCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchCode)) return [];
        return (await branches.GetOpenCounterCodesAsync(new BranchCode(branchCode), cancellationToken).ConfigureAwait(false))
            .Select(code => new SelectListItem(code, code))
            .ToArray();
    }

    public async Task<IReadOnlyList<SelectListItem>> ProductsAsync(CancellationToken cancellationToken) =>
        (await catalogue.GetCatalogueAsync(null, cancellationToken).ConfigureAwait(false))
            .Select(product => new SelectListItem($"{product.ProductId} - {product.Name} ({product.UnitPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})", product.ProductId))
            .ToArray();

    public async Task<IReadOnlyList<SelectListItem>> ZonesAsync(CancellationToken cancellationToken) =>
        (await rates.GetRatesAsync(cancellationToken).ConfigureAwait(false))
            .Select(rate => rate.DestinationZoneCode.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(zone => new SelectListItem(zone, zone))
            .ToArray();
}
