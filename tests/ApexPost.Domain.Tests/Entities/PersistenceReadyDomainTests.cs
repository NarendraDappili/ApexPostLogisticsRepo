using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

public class PersistenceReadyDomainTests
{
    [Theory]
    [InlineData("stp-1st-10", "STP-1ST-10")]
    [InlineData("  box-l ", "BOX-L")]
    public void Sku_is_normalized_to_an_upper_case_code(string raw, string expected) =>
        Assert.Equal(expected, new Sku(raw).Value);

    [Theory]
    [InlineData("")]
    [InlineData("HAS SPACE")]
    [InlineData("UNDER_SCORE")]
    [InlineData("A-SKU-THAT-IS-LONGER-THAN-32-CHARS")]
    public void Sku_rejects_values_that_do_not_fit_a_short_code(string raw) =>
        Assert.ThrowsAny<ArgumentException>(() => new Sku(raw));

    [Fact]
    public void Parcel_weight_is_limited_to_the_counter_acceptance_limit()
    {
        Assert.Equal(30m, new ParcelWeight(ParcelWeight.MaximumKilograms).Kilograms);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelWeight(30.001m));
    }

    [Fact]
    public void Product_defaults_its_sku_and_category_and_can_be_recategorized()
    {
        var product = new Product(new ProductId("prd-001"), "Envelope", new Money(1m));
        Assert.Equal(("PRD-001", ProductCategory.GeneralCode), (product.Sku.Value, product.CategoryCode));

        var labelled = new Product(new ProductId("PRD-002"), "Stamp", new Money(1m), new Sku("stp-1"), "stamps");
        labelled.MoveToCategory("packaging");

        Assert.Equal(("STP-1", "PACKAGING"), (labelled.Sku.Value, labelled.CategoryCode));
    }

    [Fact]
    public void Category_requires_a_short_code_and_a_name()
    {
        var category = new ProductCategory(" stamps ", "Stamps");
        category.Rename("Postage stamps");

        Assert.Equal(("STAMPS", "Postage stamps"), (category.Code, category.Name));
        Assert.Throws<ArgumentException>(() => new ProductCategory("A-CATEGORY-CODE-TOO-LONG", "Long"));
        Assert.Throws<ArgumentException>(() => category.Rename(" "));
    }

    [Fact]
    public void Transaction_payment_settles_the_full_total()
    {
        var transaction = new CounterTransaction(
            new("TX-1"), new("BR-1"), new("C-01"), new("STF-1"),
            [new CounterTransactionLine(new ProductId("P-1"), "Envelope", 2, new Money(5m))],
            new Money(10m), new Money(2m), PaymentMethod.Card);

        Assert.Equal(new Payment(PaymentMethod.Card, new Money(12m)), transaction.Payment);
        Assert.Equal(PaymentMethod.Card, transaction.PaymentMethod);
    }

    [Fact]
    public void A_payment_must_be_greater_than_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Payment(PaymentMethod.Cash, new Money(0m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CounterTransaction(
            new("TX-FREE"), new("BR-1"), new("C-01"), new("STF-1"),
            [new CounterTransactionLine(new ProductId("P-1"), "Free leaflet", 1, new Money(0m))],
            new Money(0m), new Money(0m), PaymentMethod.Cash));
    }

    [Fact]
    public void Stock_adjustment_changes_quantity_and_returns_its_history_entry()
    {
        var item = new BranchInventoryItem(new BranchCode("BR-1"), new ProductId("P-1"), 5);

        var adjustment = item.Adjust(-3, " Damaged in transit ", new StaffId("STF-MGR"));

        Assert.Equal(2, item.Quantity);
        Assert.Equal((-3, "Damaged in transit", "STF-MGR"), (adjustment.Delta, adjustment.Reason, adjustment.ChangedBy.Value));
    }

    [Fact]
    public void Stock_adjustment_cannot_drive_stock_negative_or_be_empty()
    {
        var item = new BranchInventoryItem(new BranchCode("BR-1"), new ProductId("P-1"), 2);

        Assert.Throws<InvalidOperationException>(() => item.Adjust(-3, "Count", new StaffId("STF-MGR")));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Adjust(0, "Count", new StaffId("STF-MGR")));
        Assert.Throws<ArgumentException>(() => item.Adjust(1, " ", new StaffId("STF-MGR")));
        Assert.Throws<ArgumentException>(() => item.Adjust(1, new string('x', StockAdjustment.MaxReasonLength + 1), new StaffId("STF-MGR")));
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public void High_value_money_order_is_cleared_only_after_successful_verification()
    {
        var threshold = new Money(1_000m);
        var order = new MoneyOrder(new("MO-1"), new("BR-1"), new Money(5_000m), "Sender", "Recipient", new Money(2.5m), new("TX-1"));
        var small = new MoneyOrder(new("MO-2"), new("BR-1"), new Money(1_000m), "Sender", "Recipient", new Money(2.5m), new("TX-1"));

        Assert.True(order.RequiresVerification(threshold));
        Assert.False(order.IsClearedForPayment(threshold));
        Assert.False(small.RequiresVerification(threshold));
        Assert.True(small.IsClearedForPayment(threshold));

        order.RecordIdentityVerification(new IdentityVerificationRecord(new("TX-1"), "Passport", MaskedIdentityDocumentReference.FromDocumentNumber("AB123456"), IdentityVerificationOutcome.Verified));

        Assert.True(order.IsClearedForPayment(threshold));
        Assert.Equal("****3456", order.IdentityVerification!.MaskedDocumentReference.Value);
    }
}
