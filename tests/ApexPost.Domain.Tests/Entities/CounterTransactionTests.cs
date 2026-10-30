using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

public class CounterTransactionTests
{
    [Fact]
    public void A_paid_sale_can_be_voided_with_a_reason_but_not_voided_or_refunded_again()
    {
        var transaction = Paid();

        transaction.Void(new StaffId("MANAGER-1"), " keyed in error ");

        Assert.Equal(CounterTransactionStatus.Voided, transaction.Status);
        Assert.Equal("keyed in error", transaction.StatusReason);
        Assert.Throws<InvalidOperationException>(() => transaction.Void(new StaffId("MANAGER-1"), "again"));
        Assert.Throws<InvalidOperationException>(() => transaction.Refund(new StaffId("MANAGER-1"), "again"));
    }

    [Fact]
    public void A_reversal_requires_a_reason()
    {
        Assert.ThrowsAny<ArgumentException>(() => Paid().Void(new StaffId("MANAGER-1"), " "));
        Assert.ThrowsAny<ArgumentException>(() => Paid().Refund(new StaffId("MANAGER-1"), ""));
    }

    [Fact]
    public void Basket_summary_properties_split_retail_and_services()
    {
        var transaction = Paid();

        Assert.Equal(20m, transaction.RetailTotal.Amount);
        Assert.Equal(8.5m, transaction.ServiceTotal.Amount);
    }

    private static CounterTransaction Paid()
    {
        var transaction = new CounterTransaction(new("TX-1"), new("BR-1"), new("C-1"), new("CLERK-1"),
            [new(new ProductId("P-1"), "Envelope", 2, new(10m)), new(null, "Standard parcel", 1, new(8.5m))],
            new(28.5m), new(0m), PaymentMethod.Card);
        transaction.MarkPaid();
        return transaction;
    }
}
