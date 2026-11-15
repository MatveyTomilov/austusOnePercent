using System;
using System.Linq;

namespace FaustusOnePercent.AutoSeller;

public static class PricingEngine
{
    public static SaleQuote Calculate(FavoritePair pair, int quantity, MarketBook book, SellerPolicy policy,
        decimal minimumPrice = 0, DateTimeOffset? now = null)
    {
        if (quantity <= 0 || quantity < policy.MinimumQuantity) return SaleQuote.Invalid("Below minimum quantity");
        if (minimumPrice < 0 || policy.UndercutPercent < 0 || policy.UndercutPercent >= 100
            || policy.MaximumActualDiscount < 0 || policy.MaximumActualDiscount >= 100
            || (policy.Mode == PricingMode.Undercut && policy.MaximumActualDiscount < policy.UndercutPercent))
            return SaleQuote.Invalid("Invalid pricing limits");
        if (book == null || book.Pair.Key != pair.Key) return SaleQuote.Invalid("Wrong market direction / pair");
        if (!book.DirectionVerified) return SaleQuote.Invalid("Order-book direction not verified; inspect both feeds");
        var age = (now ?? DateTimeOffset.UtcNow) - book.ReadAt;
        if (age.TotalSeconds < -1 || age.TotalSeconds > policy.MaximumQuoteAgeSeconds)
            return SaleQuote.Invalid("Market data expired");
        var source = policy.Mode == PricingMode.InstantExchange ? book.CounterOrders : book.Competitors;
        var offers = source.Where(o => o.Want > 0 && o.Have > 0 && o.ListedCount > 0).ToArray();
        if (offers.Length == 0) return SaleQuote.Invalid("No valid offers for this pricing mode");
        var benchmark = offers.Aggregate((best, candidate) =>
        {
            long left = (long)candidate.Want * best.Have;
            long right = (long)best.Want * candidate.Have;
            return (policy.Mode == PricingMode.InstantExchange ? left > right : left < right) ? candidate : best;
        });
        decimal reference = (decimal)benchmark.Want / benchmark.Have;
        decimal discount = policy.Mode == PricingMode.Undercut ? policy.UndercutPercent : 0;
        long numerator = (long)quantity * benchmark.Want;
        // Exact integer math: ceil(n/d)-1 = floor((n-1)/d). Dividing a repeating
        // decimal first can turn an exact 18 into 18.000...001 and fail to beat it.
        decimal raw = policy.Mode == PricingMode.BeatCompetitor
            ? (numerator - 1) / benchmark.Have
            : policy.Mode == PricingMode.OneUnitBelowSeller ? numerator / benchmark.Have - 1
            : policy.Mode == PricingMode.Undercut
                ? decimal.Floor(numerator * (1 - discount / 100) / benchmark.Have)
                : numerator / benchmark.Have;
        if (raw < 1) return SaleQuote.Invalid("Would receive zero; increase the lot");
        if (raw > int.MaxValue) return SaleQuote.Invalid("Result exceeds integer limit");
        var actual = 100 * (numerator - raw * benchmark.Have) / numerator;
        if (actual > policy.MaximumActualDiscount + 0.00000001m)
            return SaleQuote.Invalid($"Rounding discount {actual:0.####}% exceeds limit");
        var price = raw / quantity;
        if (price < minimumPrice) return SaleQuote.Invalid("Below minimum acceptable price");
        int want = (int)raw;
        int a = quantity, b = want;
        while (b != 0) { int t = a % b; a = b; b = t; }
        return new(true, "", want, quantity, reference, price, actual, quantity / a);
    }
}
