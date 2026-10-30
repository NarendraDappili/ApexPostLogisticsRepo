namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// C# 14 extension members that keep the platform's rounding and summing rules beside Money,
/// so tax, fee and report code cannot each pick a different rounding mode.
/// </summary>
public static class MoneyExtensions
{
    extension(Money money)
    {
        /// <summary>Rounds to whole cents using the platform's away-from-zero policy.</summary>
        public Money RoundedToCents => new(decimal.Round(money.Amount, 2, MidpointRounding.AwayFromZero));
    }

    extension(IEnumerable<Money> amounts)
    {
        public Money Total => new(amounts.Sum(amount => amount.Amount));
    }
}
