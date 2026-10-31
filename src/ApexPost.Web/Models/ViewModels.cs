using System.ComponentModel.DataAnnotations;
using ApexPost.Application.Queries;
using ApexPost.Application.Reports;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Web.Models;

public sealed class LoginViewModel
{
    [Required, StringLength(80)] public string StaffId { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), StringLength(256)] public string Password { get; set; } = string.Empty;
}

public sealed record BranchDashboardViewModel(string BranchCode, BranchDashboard? Dashboard, string? Message = null);
public sealed record CatalogueItemViewModel(string ProductId, string Sku, string Name, string CategoryCode, decimal UnitPrice, int? StockQuantity);
public sealed record CatalogueViewModel(string BranchCode, IReadOnlyCollection<CatalogueItemViewModel> Products, string? Message = null, IReadOnlyCollection<CategoryItem>? Categories = null);

/// <summary>Branch Manager form for a stock correction at their own branch.</summary>
public sealed class StockAdjustmentViewModel
{
    [Required, StringLength(48), Display(Name = "Product")] public string ProductId { get; set; } = string.Empty;
    [Range(-10000, 10000), Display(Name = "Change (units)")] public int Delta { get; set; }
    [Required, StringLength(StockAdjustment.MaxReasonLength)] public string Reason { get; set; } = string.Empty;
}

/// <summary>Administrator form for a set-based price revision of one category.</summary>
public sealed class PriceRevisionViewModel
{
    [Required, StringLength(ProductCategory.MaxCodeLength), Display(Name = "Category")] public string CategoryCode { get; set; } = string.Empty;
    [Range(typeof(decimal), "-50", "100"), Display(Name = "Change (%)")] public decimal Percentage { get; set; }
}

public sealed class CounterBasketViewModel
{
    [Required, StringLength(48)] public string TransactionId { get; set; } = string.Empty;
    [Required, StringLength(48)] public string BranchCode { get; set; } = string.Empty;
    [Required, StringLength(48)] public string CounterCode { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public List<RetailLineInput> RetailLines { get; set; } = [];
    [StringLength(1000)] public string? ShipmentIds { get; set; }
    [StringLength(1000)] public string? MoneyOrderIds { get; set; }

    public const int RetailRowCount = 5;

    /// <summary>Pads the basket to a fixed number of retail rows so the form works without JavaScript.</summary>
    public CounterBasketViewModel WithBlankRetailRows()
    {
        while (RetailLines.Count < RetailRowCount) RetailLines.Add(new RetailLineInput());
        return this;
    }
}

public sealed class RetailLineInput
{
    [StringLength(48)] public string? ProductId { get; set; }
    [Range(1, 10000)] public int? Quantity { get; set; }
}

public sealed record TransactionSummaryViewModel(string TransactionId, DateTimeOffset OccurredAtUtc, string CounterCode, string ClerkId, decimal Total, CounterTransactionStatus Status, string? StatusReason)
{
    public bool CanVoid => Status is CounterTransactionStatus.Open or CounterTransactionStatus.Paid;
    public bool CanRefund => Status == CounterTransactionStatus.Paid;
}

public sealed record TransactionListViewModel(string BranchCode, IReadOnlyCollection<TransactionSummaryViewModel> Transactions);

public sealed class ShipmentFormViewModel
{
    [Required, StringLength(48)] public string ShipmentId { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Transaction ID")] public string TransactionId { get; set; } = string.Empty;
    [Required, StringLength(48)] public string OriginBranchCode { get; set; } = string.Empty;
    [Required, StringLength(48)] public string DestinationZoneCode { get; set; } = string.Empty;
    public ShipmentType ShipmentType { get; set; }
    public ShippingServiceLevel ServiceLevel { get; set; }
    [Range(0.001, ParcelWeight.MaximumKilograms)] public decimal WeightKilograms { get; set; }
    [Range(typeof(decimal), "0.001", "100000")] public decimal LengthCentimetres { get; set; }
    [Range(typeof(decimal), "0.001", "100000")] public decimal WidthCentimetres { get; set; }
    [Range(typeof(decimal), "0.001", "100000")] public decimal HeightCentimetres { get; set; }
}

public sealed record ShippingLabelViewModel(Shipment Shipment, decimal? Charge = null);

public sealed class MoneyOrderFormViewModel
{
    [Required, StringLength(48)] public string MoneyOrderId { get; set; } = string.Empty;
    [Required, StringLength(48)] public string BranchCode { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "999999999.99")] public decimal Amount { get; set; }
    [Required, StringLength(160)] public string Sender { get; set; } = string.Empty;
    [Required, StringLength(160)] public string Recipient { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Transaction ID")] public string TransactionContextId { get; set; } = string.Empty;
}

/// <summary>Identity document input for the separate verification step. The outcome is decided on the server.</summary>
public sealed class MoneyOrderVerificationViewModel
{
    [Required, StringLength(48)] public string MoneyOrderId { get; set; } = string.Empty;
    [Required, StringLength(80), Display(Name = "Document type")] public string DocumentType { get; set; } = string.Empty;
    [Required, StringLength(128), Display(Name = "Document number")] public string DocumentNumber { get; set; } = string.Empty;
    public MoneyOrderSummaryViewModel? Summary { get; set; }
}

public sealed record MoneyOrderSummaryViewModel(string TransactionId, decimal Amount, decimal Fee, decimal Total, bool RequiresVerification, IdentityVerificationOutcome Outcome, string? MaskedDocumentReference);

public sealed class StaffFormViewModel
{
    [Required, StringLength(48), Display(Name = "Staff ID")] public string StaffId { get; set; } = string.Empty;
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    public StaffRole Role { get; set; }
    [Required, StringLength(48), Display(Name = "Home branch")] public string BranchCode { get; set; } = string.Empty;
    [DataType(DataType.Password), StringLength(256, MinimumLength = 10), Display(Name = "Password")] public string? Password { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BranchFormViewModel
{
    [Required, StringLength(48), Display(Name = "Branch code")] public string BranchCode { get; set; } = string.Empty;
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Region { get; set; } = string.Empty;
    [Required, StringLength(120), Display(Name = "Opening hours")] public string OpeningHours { get; set; } = string.Empty;
    [Range(1, 20), Display(Name = "Counter terminals")] public int CounterCount { get; set; } = 2;
    public bool IsOpen { get; set; } = true;
}

public sealed record ReportsViewModel(
    string BranchCode,
    DateOnly Day,
    DailyBranchSalesReport DailySales,
    LowStockReport LowStock,
    ShippingVolumeReport Shipping,
    MoneyOrderVerificationReport MoneyOrders);

public sealed class TransactionReversalViewModel
{
    [Required, StringLength(48)] public string TransactionId { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
}
