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

/// <summary>
/// A row of the shipping rate table. The Application's <see cref="Application.Pricing.ShippingRate"/> stays a pure
/// pricing type with no persistence concerns; <c>EfShippingRateProvider</c> maps rows to it, and <see cref="From"/> maps back.
/// </summary>
public sealed class ShippingRateEntity
{
    public ShipmentType ShipmentType { get; set; }

    public ShippingServiceLevel ServiceLevel { get; set; }

    public ZoneCode DestinationZoneCode { get; set; } = null!;

    public Money Charge { get; set; } = null!;

    public ParcelWeight MaximumWeight { get; set; } = null!;

    public static ShippingRateEntity From(Application.Pricing.ShippingRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        return new ShippingRateEntity
        {
            ShipmentType = rate.ShipmentType,
            ServiceLevel = rate.ServiceLevel,
            DestinationZoneCode = rate.DestinationZoneCode,
            Charge = rate.Charge,
            MaximumWeight = rate.MaximumWeight,
        };
    }

    /// <summary>The validated pricing rate; the constructor re-checks the stored enum values.</summary>
    public Application.Pricing.ShippingRate ToRate() => new(ShipmentType, ServiceLevel, DestinationZoneCode, Charge, MaximumWeight);
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

/// <summary>
/// One inserted, updated or deleted row, written in the same SaveChanges (and so the same transaction) as the
/// change itself. Secrets such as password hashes are never stored, only the fact that they changed.
/// </summary>
public sealed class DataChangeEntity
{
    public long DataChangeId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? StaffId { get; set; }

    /// <summary>The branch the row belongs to, when it has one; otherwise the branch of the staff member.</summary>
    public string? BranchCode { get; set; }

    public string TableName { get; set; } = string.Empty;

    /// <summary>The row's business key, such as <c>TransactionCode=TX-1</c>, never its surrogate identity.</summary>
    public string RecordKey { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    /// <summary>A JSON array of <c>{ column, old, new }</c> objects.</summary>
    public string ChangesJson { get; set; } = "[]";

    public string? CorrelationId { get; set; }
}

/// <summary>One sign-in or access event. No password or request body is ever stored.</summary>
public sealed class SecurityEventEntity
{
    public long SecurityEventId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string EventType { get; set; } = string.Empty;

    /// <summary>The signed-in staff member, or for a failed sign-in the staff ID that was typed.</summary>
    public string? StaffId { get; set; }

    public string? Role { get; set; }

    public string? BranchCode { get; set; }

    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>The request path without its query string.</summary>
    public string Path { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string? RemoteIpAddress { get; set; }

    /// <summary>A short reason for auditors, such as <c>UnknownStaff</c>; never shown to the person signing in.</summary>
    public string? Detail { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}
