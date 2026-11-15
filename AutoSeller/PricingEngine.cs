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
        decimal reference = policy.Mode == PricingMode.InstantExchange
            ? offers.Max(o => (decimal)o.Want / o.Have) : offers.Min(o => (decimal)o.Want / o.Have);
        decimal discount = policy.Mode == PricingMode.Undercut ? policy.UndercutPercent : 0;
        decimal fair = quantity * reference;
        // At a fixed HAVE amount, floor WANT. Never invert the ratio or silently change volume.
        decimal raw = policy.Mode == PricingMode.BeatCompetitor
            ? decimal.Ceiling(fair) - 1 : decimal.Floor(fair * (1 - discount / 100));
        if (raw < 1) return SaleQuote.Invalid("Would receive zero; increase the lot");
        if (raw > int.MaxValue) return SaleQuote.Invalid("Result exceeds integer limit");
        var actual = 100 * (1 - raw / fair);
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
