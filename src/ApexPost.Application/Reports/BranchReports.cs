using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Reports;

/// <summary>
/// The four branch reports. Implementations aggregate in the database (projections or stored procedures)
/// instead of loading aggregates and summing them in memory.
/// </summary>
public interface IBranchReportQueries
{
    /// <summary>Paid sales on one UTC day, split into retail (catalogue products) and services (shipments, money orders).</summary>
    Task<DailyBranchSalesReport> GetDailySalesAsync(BranchCode branchCode, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paid sales for each UTC day from <paramref name="from"/> to <paramref name="to"/>, both included, at most
    /// <see cref="BranchSalesPeriodReport.MaxDays"/> days. Days without a paid sale are left out.
    /// </summary>
    Task<BranchSalesPeriodReport> GetSalesForPeriodAsync(BranchCode branchCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<LowStockReport> GetLowStockAsync(BranchCode branchCode, int threshold, CancellationToken cancellationToken = default);

    Task<ShippingVolumeReport> GetShippingVolumeAsync(BranchCode branchCode, CancellationToken cancellationToken = default);

    Task<MoneyOrderVerificationReport> GetMoneyOrderVerificationAsync(BranchCode branchCode, CancellationToken cancellationToken = default);
}

public sealed record DailyBranchSalesReport(BranchCode BranchCode, DateOnly Day, Money RetailSales, Money ServiceSales, Money Tax, int PaidTransactionCount)
{
    public Money TotalSales => new(RetailSales.Amount + ServiceSales.Amount + Tax.Amount);
}

/// <summary>Paid sales over a period: one entry per day that had a paid sale, and the period's totals.</summary>
public sealed record BranchSalesPeriodReport(BranchCode BranchCode, DateOnly From, DateOnly To, IReadOnlyList<DailyBranchSalesReport> Days)
{
    /// <summary>The longest period a report may cover: a year, including a leap day.</summary>
    public const int MaxDays = 366;

    public Money RetailSales => new(Days.Sum(day => day.RetailSales.Amount));
    public Money ServiceSales => new(Days.Sum(day => day.ServiceSales.Amount));
    public Money Tax => new(Days.Sum(day => day.Tax.Amount));
    public Money TotalSales => new(Days.Sum(day => day.TotalSales.Amount));
    public int PaidTransactionCount => Days.Sum(day => day.PaidTransactionCount);
    public bool IsSingleDay => From == To;

    /// <summary>A one-day period from the daily report, leaving the day out when it had no paid sale.</summary>
    public static BranchSalesPeriodReport ForDay(DailyBranchSalesReport day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return new(day.BranchCode, day.Day, day.Day, day.PaidTransactionCount > 0 ? [day] : []);
    }

    /// <summary>Why a requested period cannot be reported, or null when it can: the start must be on or before the end,
    /// and the period at most <see cref="MaxDays"/> days.</summary>
    public static string? FindProblem(DateOnly from, DateOnly to) =>
        from > to ? "The start date must be on or before the end date."
        : to.DayNumber - from.DayNumber >= MaxDays ? $"The period can be at most {MaxDays} days."
        : null;

    /// <summary>Throws <see cref="ArgumentException"/> for a period <see cref="FindProblem"/> rejects.</summary>
    public static void Validate(DateOnly from, DateOnly to)
    {
        if (FindProblem(from, to) is { } problem) throw new ArgumentException(problem);
    }
}

public sealed record LowStockReport(BranchCode BranchCode, int Threshold, IReadOnlyCollection<LowStockItem> Items);

public sealed record LowStockItem(ProductId ProductId, string Name, int Quantity);

public sealed record ShippingVolumeReport(BranchCode BranchCode, IReadOnlyCollection<ShippingVolumeItem> Items);

public sealed record ShippingVolumeItem(ZoneCode DestinationZoneCode, ShippingServiceLevel ServiceLevel, int Count);

public sealed record MoneyOrderVerificationReport(BranchCode BranchCode, int TotalCount, int VerifiedCount, int FailedCount, int PendingCount);

/// <summary>The four reports the reports page shows together. Sales cover the chosen period; the others are current.</summary>
public sealed record BranchReportPage(
    BranchSalesPeriodReport Sales, LowStockReport LowStock, ShippingVolumeReport ShippingVolume, MoneyOrderVerificationReport MoneyOrders);

/// <summary>
/// Reads the four branch reports at once. They are independent, read-only queries, so an implementation may run them
/// in parallel, provided each one has its own database context (ADR 0021).
/// </summary>
public interface IBranchReportPageQueries
{
    /// <summary>The reports with sales from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    Task<BranchReportPage> GetAsync(BranchCode branchCode, DateOnly from, DateOnly to, int lowStockThreshold, CancellationToken cancellationToken = default);

    /// <summary>The reports with the sales of one day.</summary>
    Task<BranchReportPage> GetAsync(BranchCode branchCode, DateOnly day, int lowStockThreshold, CancellationToken cancellationToken = default) =>
        GetAsync(branchCode, day, day, lowStockThreshold, cancellationToken);
}
