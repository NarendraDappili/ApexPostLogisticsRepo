using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

/// <summary>
/// Identity by business key for the entities the Week 2 tests did not cover, and the codes every key is made of.
/// EF Core's change tracker and the in-memory sets and dictionaries in the services rely on these.
/// </summary>
public sealed class IdentityAndCodeTests
{
    [Fact]
    public void A_counter_is_identified_by_its_code_within_its_branch()
    {
        var counter = new Counter(new("C-1"), new("BR-1"));

        Assert.Equal(counter, new Counter(new(" c-1 "), new("br-1")));
        Assert.Equal(counter.GetHashCode(), new Counter(new("C-1"), new("BR-1")).GetHashCode());
        // The same counter code at another branch is another counter.
        Assert.NotEqual(counter, new Counter(new("C-1"), new("BR-2")));
        Assert.False(counter.Equals((object?)null));
        Assert.False(counter.Equals("C-1"));
    }

    [Fact]
    public void A_counter_can_be_closed_and_opened_again()
    {
        var counter = new Counter(new("C-1"), new("BR-1"));

        counter.Close();
        Assert.False(counter.IsOpen);
        counter.Open();
        Assert.True(counter.IsOpen);
    }

    [Fact]
    public void A_stock_row_is_identified_by_branch_and_product_not_by_its_quantity()
    {
        var stock = new BranchInventoryItem(new("BR-1"), new("PRD-1"), 5);

        Assert.True(stock.Equals((object)new BranchInventoryItem(new("BR-1"), new("PRD-1"), 99)));
        Assert.Equal(stock.GetHashCode(), new BranchInventoryItem(new("BR-1"), new("PRD-1")).GetHashCode());
        Assert.NotEqual(stock, new BranchInventoryItem(new("BR-2"), new("PRD-1"), 5));
        Assert.NotEqual(stock, new BranchInventoryItem(new("BR-1"), new("PRD-2"), 5));
    }

    [Fact]
    public void A_transaction_is_identified_by_its_transaction_id_whatever_its_contents()
    {
        var sale = Transaction("TX-1", quantity: 1);

        Assert.True(sale.Equals((object)Transaction("tx-1", quantity: 3)));
        Assert.Equal(sale.GetHashCode(), Transaction("TX-1", quantity: 2).GetHashCode());
        Assert.NotEqual(sale, Transaction("TX-2", quantity: 1));
        Assert.Equal("C-1", sale.CounterCode.Value);
    }

    [Fact]
    public void A_category_is_identified_by_its_code_and_keeps_it_when_renamed()
    {
        var category = new ProductCategory("stamps", "Stamps");
        var renamed = new ProductCategory("STAMPS", "Postage Stamps");

        Assert.True(category.Equals((object)renamed));
        Assert.Equal(category.GetHashCode(), renamed.GetHashCode());
        category.Rename("  Postage  ");
        Assert.Equal(("STAMPS", "Postage"), (category.Code, category.Name));
        Assert.Throws<ArgumentException>(() => category.Rename(" "));
    }

    [Fact]
    public void A_money_order_rejects_a_verification_from_another_money_order_context()
    {
        var order = new MoneyOrder(new("MO-1"), new("BR-1"), new Money(5_000m), "Sender", "Recipient", new Money(2.5m), new("TX-1"));
        var otherContext = new IdentityVerificationRecord(new("TX-2"), "Passport", MaskedIdentityDocumentReference.FromDocumentNumber("AB123456"), IdentityVerificationOutcome.Verified);

        Assert.Throws<InvalidOperationException>(() => order.RecordIdentityVerification(otherContext));
        Assert.Null(order.IdentityVerification);
        Assert.True(order.Equals((object)new MoneyOrder(new("mo-1"), new("BR-9"), new Money(1m), "A", "B", new Money(0m), new("TX-9"))));
    }

    [Fact]
    public void Codes_are_trimmed_and_upper_cased_print_as_their_value_and_compare_by_value()
    {
        Assert.Equal("BR-NORTH", new BranchCode("  br-north ").ToString());
        Assert.Equal("C-01", new CounterCode("c-01").ToString());
        Assert.Equal("STF-CLERK-01", new StaffId("stf-clerk-01").ToString());
        Assert.Equal("PRD-001", new ProductId(" prd-001").ToString());
        Assert.Equal("SHP-1", new ShipmentId("shp-1").ToString());
        Assert.Equal("MO-1", new MoneyOrderId("mo-1").ToString());
        Assert.Equal(new CounterTransactionId("tx-1"), new CounterTransactionId("TX-1"));
        Assert.Equal(new MoneyOrderContextId("tx-1"), new MoneyOrderContextId(" TX-1 "));
        Assert.NotEqual(new BranchCode("BR-1"), new BranchCode("BR-2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_code_is_rejected_for_every_kind_of_code(string blank)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BranchCode(blank));
        Assert.ThrowsAny<ArgumentException>(() => new CounterCode(blank));
        Assert.ThrowsAny<ArgumentException>(() => new StaffId(blank));
        Assert.ThrowsAny<ArgumentException>(() => new ProductId(blank));
        Assert.ThrowsAny<ArgumentException>(() => new ShipmentId(blank));
        Assert.ThrowsAny<ArgumentException>(() => new MoneyOrderId(blank));
        Assert.ThrowsAny<ArgumentException>(() => new CounterTransactionId(blank));
        Assert.ThrowsAny<ArgumentException>(() => new ZoneCode(blank));
    }

    [Theory]
    [InlineData("AB123456", "****3456")]
    [InlineData("  AB123456  ", "****3456")]
    // A number of four characters or fewer would be revealed whole by its last four, so none of it is kept.
    [InlineData("1234", "****")]
    [InlineData("12", "****")]
    public void A_document_number_is_kept_only_as_its_last_four_characters(string documentNumber, string masked)
    {
        Assert.Equal(masked, MaskedIdentityDocumentReference.FromDocumentNumber(documentNumber).Value);
    }

    [Fact]
    public void A_stored_reference_that_is_not_masked_is_refused()
    {
        Assert.Throws<ArgumentException>(() => MaskedIdentityDocumentReference.FromMaskedValue("AB123456"));
        Assert.Equal("****3456", MaskedIdentityDocumentReference.FromMaskedValue("****3456").ToString());
    }

    private static CounterTransaction Transaction(string id, int quantity) =>
        new(new(id), new("BR-1"), new("C-1"), new("CLERK-1"), [new(new ProductId("P-1"), "Envelope", quantity, new(10m))], new(10m * quantity), new(0m), PaymentMethod.Card);
}
