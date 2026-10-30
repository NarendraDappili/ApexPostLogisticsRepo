using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ApexPost.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace ApexPost.Infrastructure.Persistence.Configurations;

internal sealed class CounterTransactionConfiguration : IEntityTypeConfiguration<CounterTransaction>
{
    public void Configure(EntityTypeBuilder<CounterTransaction> builder)
    {
        builder.ToTable("CounterTransactions", table =>
        {
            table.HasCheckConstraint("CK_CounterTransactions_Amounts", "[Subtotal] >= 0 AND [Tax] >= 0");
            table.HasCheckConstraint("CK_CounterTransactions_Status", InEnum<CounterTransactionStatus>("Status"));
            // A reversal always records who authorised it and why; an open or paid sale never does.
            table.HasCheckConstraint(
                "CK_CounterTransactions_Reversal",
                "([Status] IN ('Open', 'Paid') AND [AuthorizedByStaffCode] IS NULL AND [StatusReason] IS NULL) OR " +
                "([Status] IN ('Voided', 'Refunded') AND [AuthorizedByStaffCode] IS NOT NULL AND [StatusReason] IS NOT NULL)");
        });
        builder.HasSurrogateKey("CounterTransactionId", large: true);
        builder.Property(transaction => transaction.Id).HasColumnName("TransactionCode");
        builder.Property(transaction => transaction.BranchCode);
        builder.Property(transaction => transaction.CounterCode);
        builder.Property(transaction => transaction.Subtotal);
        builder.Property(transaction => transaction.Tax);
        builder.Property(transaction => transaction.OccurredAtUtc);
        builder.HasAlternateKey(transaction => transaction.Id).HasName("AK_CounterTransactions_TransactionCode");
        // The counter FK also proves the branch exists, so a second FK to Branches would be redundant.
        builder.HasOne<Counter>().WithMany()
            .HasForeignKey(transaction => new { transaction.BranchCode, transaction.CounterCode })
            .HasPrincipalKey(counter => new { counter.BranchCode, counter.Code })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(transaction => transaction.ClerkId).HasColumnName("ClerkStaffCode");
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(transaction => transaction.ClerkId).HasPrincipalKey(staff => staff.Id).OnDelete(DeleteBehavior.Restrict);
        builder.Property(transaction => transaction.AuthorizedBy).HasColumnName("AuthorizedByStaffCode");
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(transaction => transaction.AuthorizedBy).HasPrincipalKey(staff => staff.Id).OnDelete(DeleteBehavior.Restrict);
        builder.Property(transaction => transaction.StatusReason).HasMaxLength(500);
        builder.Property<byte[]>(ShadowColumns.RowVersion).IsRowVersion().IsRequired();

        // Lines and the payment have no life outside their transaction: owned, stored in their own tables,
        // and deleted with the owner (cascade) if a transaction row is ever removed.
        builder.OwnsMany(transaction => transaction.Lines, lines =>
        {
            lines.ToTable("TransactionLines", table =>
            {
                table.HasCheckConstraint("CK_TransactionLines_Quantity", "[Quantity] > 0");
                table.HasCheckConstraint("CK_TransactionLines_UnitPrice", "[UnitPrice] >= 0");
            });
            lines.WithOwner().HasForeignKey("CounterTransactionId");
            lines.Property<long>("TransactionLineId").UseIdentityColumn();
            lines.HasKey("TransactionLineId");
            lines.Property(line => line.ProductId).HasColumnName("ProductCode");
            lines.HasOne<Product>().WithMany().HasForeignKey(line => line.ProductId).HasPrincipalKey(product => product.Id).OnDelete(DeleteBehavior.Restrict);
            lines.Property(line => line.Description).HasMaxLength(250);
            lines.Property(line => line.Quantity);
            lines.Property(line => line.UnitPrice);
            lines.HasAuditColumns();
        });
        builder.Navigation(transaction => transaction.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsOne(transaction => transaction.Payment, payment =>
        {
            payment.ToTable("Payments", table =>
            {
                table.HasCheckConstraint("CK_Payments_Amount", "[Amount] >= 0");
                table.HasCheckConstraint("CK_Payments_PaymentMethod", InEnum<PaymentMethod>("PaymentMethod"));
            });
            payment.WithOwner().HasForeignKey("CounterTransactionId");
            payment.Property(value => value.Method).HasColumnName("PaymentMethod");
            payment.Property(value => value.Amount).HasColumnName("Amount");
            payment.HasAuditColumns();
        });
        builder.Navigation(transaction => transaction.Payment).IsRequired();

        builder.HasAuditColumns();
    }
}

internal sealed class MoneyOrderConfiguration : IEntityTypeConfiguration<MoneyOrderEntity>
{
    public void Configure(EntityTypeBuilder<MoneyOrderEntity> builder)
    {
        builder.ToTable("MoneyOrders", table =>
        {
            table.HasCheckConstraint("CK_MoneyOrders_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_MoneyOrders_Fee", "[Fee] >= 0");
        });
        builder.HasKey(order => order.MoneyOrderId);
        builder.HasAlternateKey(order => order.MoneyOrderCode).HasName("AK_MoneyOrders_MoneyOrderCode");
        builder.HasOne<Branch>().WithMany().HasForeignKey(order => order.BranchCode).HasPrincipalKey(branch => branch.Code).OnDelete(DeleteBehavior.Restrict);
        builder.Property(order => order.Sender).HasMaxLength(NameLength);
        builder.Property(order => order.Recipient).HasMaxLength(NameLength);
        builder.Property(order => order.TransactionContextId).HasColumnName("TransactionContextCode");
        // One verification per money order; it is part of the aggregate, so it is deleted with it.
        builder.HasOne(order => order.IdentityVerification).WithOne()
            .HasForeignKey<IdentityVerificationEntity>(verification => verification.MoneyOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasAuditColumns();
    }
}

internal sealed class IdentityVerificationConfiguration : IEntityTypeConfiguration<IdentityVerificationEntity>
{
    public void Configure(EntityTypeBuilder<IdentityVerificationEntity> builder)
    {
        builder.ToTable("IdentityVerifications", table =>
        {
            // varchar(8) holds "****" plus at most four characters, so a full document number cannot fit,
            // and the CHECK rejects any value that does not start with the mask.
            table.HasCheckConstraint("CK_IdentityVerifications_Masked", "[MaskedDocumentReference] LIKE '****%'");
            table.HasCheckConstraint("CK_IdentityVerifications_Outcome", In("Outcome", [nameof(IdentityVerificationOutcome.Verified), nameof(IdentityVerificationOutcome.Failed)]));
        });
        builder.HasKey(verification => verification.IdentityVerificationId);
        builder.Property(verification => verification.DocumentType).HasMaxLength(80).IsUnicode(false);
        builder.Property(verification => verification.MaskedDocumentReference).HasMaxLength(8).IsUnicode(false);
        builder.HasAuditColumns();
    }
}
