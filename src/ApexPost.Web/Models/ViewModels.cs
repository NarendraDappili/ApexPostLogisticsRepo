using System.ComponentModel.DataAnnotations;
using ApexPost.Application.Reports;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;

namespace ApexPost.Web.Models;

public sealed class LoginViewModel
{
    [Required, StringLength(80)] public string StaffId { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), StringLength(256)] public string Password { get; set; } = string.Empty;
}

public sealed record BranchDashboardViewModel(string BranchCode, string? BranchName, bool? IsOpen, string? Message = null);
public sealed record CatalogueItemViewModel(string ProductId, string Name, decimal UnitPrice, int? StockQuantity);
public sealed record CatalogueViewModel(string BranchCode, IReadOnlyCollection<CatalogueItemViewModel> Products, string? Message = null);

public sealed class CounterBasketViewModel
{
    [Required, StringLength(48)] public string TransactionId { get; set; } = string.Empty;
    [Required, StringLength(48)] public string BranchCode { get; set; } = string.Empty;
    [Required, StringLength(48)] public string CounterCode { get; set; } = string.Empty;
    [Required, StringLength(80)] public string ClerkId { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    [StringLength(48)] public string? ProductId { get; set; }
    [Range(1, 10000)] public int? RetailQuantity { get; set; }
    [StringLength(160)] public string? ServiceDescription { get; set; }
    [Range(typeof(decimal), "0.01", "999999.99")] public decimal? ServiceUnitPrice { get; set; }
    [Range(1, 10000)] public int ServiceQuantity { get; set; } = 1;
    [StringLength(1000)] public string? MoneyOrderIds { get; set; }
}

public sealed class ShipmentFormViewModel
{
    [Required, StringLength(48)] public string ShipmentId { get; set; } = string.Empty;
    [Required, StringLength(48)] public string OriginBranchCode { get; set; } = string.Empty;
    [Required, StringLength(48)] public string DestinationZoneCode { get; set; } = string.Empty;
    public ShipmentType ShipmentType { get; set; }
    public ShippingServiceLevel ServiceLevel { get; set; }
    [Range(typeof(decimal), "0.001", "100000")] public decimal WeightKilograms { get; set; }
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
    [Range(typeof(decimal), "0", "999999.99")] public decimal Fee { get; set; }
    [Required, StringLength(48)] public string TransactionContextId { get; set; } = string.Empty;
    [StringLength(80)] public string? DocumentType { get; set; }
    [StringLength(128)] public string? DocumentNumber { get; set; }
    public IdentityVerificationOutcome VerificationOutcome { get; set; } = IdentityVerificationOutcome.Pending;
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
