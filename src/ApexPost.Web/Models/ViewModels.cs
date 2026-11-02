using System.ComponentModel.DataAnnotations;
using ApexPost.Application.Queries;
using ApexPost.Application.Reports;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Web.Models;

public sealed class LoginViewModel
{
    [Required, StringLength(80), Display(Name = "Staff ID")] public string StaffId { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), StringLength(256)] public string Password { get; set; } = string.Empty;
}

public sealed record BranchDashboardViewModel(string BranchCode, BranchDashboard? Dashboard, string? Message = null);

/// <summary>
/// The branches a user may view. With one, the picker shows it as text (clerks and managers work at their home branch
/// only); with several it is a dropdown (administrators and auditors).
/// </summary>
public sealed record BranchPickerModel(IReadOnlyList<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Branches, string ColumnClass = "col-sm-4")
{
    public bool CanChoose => Branches.Count > 1;
}
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
    [Required, StringLength(48), Display(Name = "Transaction ID")] public string TransactionId { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Branch")] public string BranchCode { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Counter")] public string CounterCode { get; set; } = string.Empty;
    [Display(Name = "Payment method")] public PaymentMethod PaymentMethod { get; set; }
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
    [Required, StringLength(48), Display(Name = "Shipment ID")] public string ShipmentId { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Transaction ID")] public string TransactionId { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Origin branch")] public string OriginBranchCode { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Destination zone")] public string DestinationZoneCode { get; set; } = string.Empty;
    [Display(Name = "Shipment type")] public ShipmentType ShipmentType { get; set; }
    [Display(Name = "Service level")] public ShippingServiceLevel ServiceLevel { get; set; }
    [Range(0.001, ParcelWeight.MaximumKilograms), Display(Name = "Weight (kg)")] public decimal WeightKilograms { get; set; }
    [Range(typeof(decimal), "0.001", "100000"), Display(Name = "Length (cm)")] public decimal LengthCentimetres { get; set; }
    [Range(typeof(decimal), "0.001", "100000"), Display(Name = "Width (cm)")] public decimal WidthCentimetres { get; set; }
    [Range(typeof(decimal), "0.001", "100000"), Display(Name = "Height (cm)")] public decimal HeightCentimetres { get; set; }
}

public sealed record ShippingLabelViewModel(ShipmentLabel Label, decimal? Charge = null);

public sealed class MoneyOrderFormViewModel
{
    [Required, StringLength(48), Display(Name = "Money order ID")] public string MoneyOrderId { get; set; } = string.Empty;
    [Required, StringLength(48), Display(Name = "Branch")] public string BranchCode { get; set; } = string.Empty;
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

/// <summary>
/// The branch form. Opening hours are picked as times for weekdays, Saturday and Sunday (empty times mean closed that
/// day) and stored as text, for example "Mon-Fri 09:00-17:30; Sat 09:00-12:30". <see cref="OpeningHours"/> carries the
/// stored text: it is kept as it is when no time is picked, for hours in a format the pickers cannot show.
/// </summary>
public sealed class BranchFormViewModel : IValidatableObject
{
    [Required, StringLength(48), Display(Name = "Branch code")] public string BranchCode { get; set; } = string.Empty;
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Region { get; set; } = string.Empty;
    [StringLength(120), Display(Name = "Opening hours")] public string? OpeningHours { get; set; }
    [Display(Name = "Monday to Friday: opens")] public TimeOnly? WeekdaysOpen { get; set; }
    [Display(Name = "Monday to Friday: closes")] public TimeOnly? WeekdaysClose { get; set; }
    [Display(Name = "Saturday: opens")] public TimeOnly? SaturdayOpen { get; set; }
    [Display(Name = "Saturday: closes")] public TimeOnly? SaturdayClose { get; set; }
    [Display(Name = "Sunday: opens")] public TimeOnly? SundayOpen { get; set; }
    [Display(Name = "Sunday: closes")] public TimeOnly? SundayClose { get; set; }
    [Range(1, 20), Display(Name = "Counter terminals")] public int CounterCount { get; set; } = 2;
    public bool IsOpen { get; set; } = true;

    /// <summary>The picked times as a schedule; days without both times are closed.</summary>
    public OpeningHoursSchedule Schedule => new(Times(WeekdaysOpen, WeekdaysClose), Times(SaturdayOpen, SaturdayClose), Times(SundayOpen, SundayClose));

    /// <summary>True when the stored text could not be shown in the pickers, so it is kept unless times are picked.</summary>
    public bool KeepsUnpickableHours => !string.IsNullOrWhiteSpace(OpeningHours) && OpeningHoursSchedule.TryParse(OpeningHours) is null;

    /// <summary>The opening-hours text to save: the picked schedule, or the stored text when no time was picked.</summary>
    public string ResolvedOpeningHours => Schedule.IsEmpty ? OpeningHours?.Trim() ?? string.Empty : Schedule.ToText();

    /// <summary>Fills the pickers from stored opening-hours text; text they cannot show stays in <see cref="OpeningHours"/>.</summary>
    public void LoadOpeningHours(string openingHours)
    {
        OpeningHours = openingHours;
        if (OpeningHoursSchedule.TryParse(openingHours) is not { } schedule) return;
        (WeekdaysOpen, WeekdaysClose) = (schedule.Weekdays?.Opens, schedule.Weekdays?.Closes);
        (SaturdayOpen, SaturdayClose) = (schedule.Saturday?.Opens, schedule.Saturday?.Closes);
        (SundayOpen, SundayClose) = (schedule.Sunday?.Opens, schedule.Sunday?.Closes);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (opens, closes, day, openField, closeField) in new[]
        {
            (WeekdaysOpen, WeekdaysClose, "Monday to Friday", nameof(WeekdaysOpen), nameof(WeekdaysClose)),
            (SaturdayOpen, SaturdayClose, "Saturday", nameof(SaturdayOpen), nameof(SaturdayClose)),
            (SundayOpen, SundayClose, "Sunday", nameof(SundayOpen), nameof(SundayClose)),
        })
        {
            if (opens is null != closes is null)
                yield return new ValidationResult($"Enter both the opening and the closing time for {day}, or leave both empty if the branch is closed.", [opens is null ? openField : closeField]);
            else if (opens is { } open && closes is { } close && close <= open)
                yield return new ValidationResult($"On {day} the branch must close after it opens.", [closeField]);
        }

        if (Schedule.IsEmpty && string.IsNullOrWhiteSpace(OpeningHours))
            yield return new ValidationResult("Enter the opening and closing times for at least one day.", [nameof(WeekdaysOpen)]);
    }

    private static OpeningTimes? Times(TimeOnly? opens, TimeOnly? closes) =>
        opens is { } open && closes is { } close && close > open ? new OpeningTimes(open, close) : null;
}

/// <summary>
/// The reports page for one branch. Sales cover <see cref="From"/> to <see cref="To"/> (both included); the other reports
/// are current. <see cref="Page"/> is null when the requested period is invalid, and <see cref="Error"/> says why.
/// </summary>
public sealed record ReportsViewModel(string BranchCode, DateOnly From, DateOnly To, DateOnly Today, BranchReportPage? Page, string? Error = null);

public sealed class TransactionReversalViewModel
{
    [Required, StringLength(48)] public string TransactionId { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
}
