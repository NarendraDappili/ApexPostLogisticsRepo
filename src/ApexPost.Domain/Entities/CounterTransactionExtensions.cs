using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

/// <summary>C# 14 extension properties that summarise a mixed basket for reporting.</summary>
public static class CounterTransactionExtensions
{
    extension(CounterTransaction transaction)
    {
        public Money RetailTotal => transaction.Lines.Where(line => line.IsRetail).Select(line => line.Total).Total;

        public Money ServiceTotal => transaction.Lines.Where(line => !line.IsRetail).Select(line => line.Total).Total;
    }
}
