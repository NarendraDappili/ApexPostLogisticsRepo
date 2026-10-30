using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Infrastructure.Persistence.Entities;

// Persistence-only shapes. They exist for tables that have no rich domain entity (lookups, audit)
// or where the stored shape deliberately differs from the domain model (money orders, see MoneyOrderMapper).

/// <summary>Lookup row for <see cref="StaffRole"/>; the key is the enum value so the staff FK stays a tinyint.</summary>
public sealed class RoleEntity
{
    public StaffRole RoleId { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>A destination zone that shipping rates and shipments refer to.</summary>
public sealed class ShippingZoneEntity
{
    public ZoneCode ZoneCode { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
}

/// <summary>Stored shape of the <see cref="Domain.Entities.MoneyOrder"/> aggregate root.</summary>
public sealed class MoneyOrderEntity
{
    public int MoneyOrderId { get; set; }

    public MoneyOrderId MoneyOrderCode { get; set; } = null!;

    public BranchCode BranchCode { get; set; } = null!;

    public decimal Amount { get; set; }

    public decimal Fee { get; set; }

    public string Sender { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public MoneyOrderContextId TransactionContextId { get; set; } = null!;

    public IdentityVerificationEntity? IdentityVerification { get; set; }
}

/// <summary>
/// The identity check for one money order. Only the masked reference is stored, and the context ID is not
/// repeated because it always equals the money order's own transaction context.
/// </summary>
public sealed class IdentityVerificationEntity
{
    public int IdentityVerificationId { get; set; }

    public int MoneyOrderId { get; set; }

    public string DocumentType { get; set; } = string.Empty;

    public string MaskedDocumentReference { get; set; } = string.Empty;

    public IdentityVerificationOutcome Outcome { get; set; }
}

/// <summary>One audited MVC action; mirrors <see cref="Application.Auditing.AuditRecord"/>.</summary>
public sealed class AuditEntryEntity
{
    public long AuditEntryId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? StaffId { get; set; }

    public string? Role { get; set; }

    public string? BranchCode { get; set; }

    public string Controller { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    /// <summary>The allow-listed route identifiers as a JSON object.</summary>
    public string RouteValuesJson { get; set; } = "{}";

    public int StatusCode { get; set; }

    public long DurationMilliseconds { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;
}
