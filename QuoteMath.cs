using System;
using System.Collections.Generic;

namespace FaustusOnePercent;

// Have = currency the player is giving; Want = currency the player receives.
public readonly record struct MarketOrder(int Want, int Have, int ListedCount);

public readonly record struct QuoteResult(
    bool IsValid,
    string Error,
    int Want,
    int Have,
    MarketOrder ReferenceOrder,
    decimal FairWant,
    decimal ActualUndercutPercent,
    int MinimalHaveLot)
{
    public static QuoteResult Invalid(string message) =>
        new(false, message, 0, 0, default, 0m, 0m, 0);
}

public static class QuoteMath
{
    private const decimal Discount = 0.01m;

    /// <summary>
    /// Chooses the lowest amount of Want per unit of Have among visible market orders.
    /// The user offers the same Have quantity and asks 1% less Want than that price.
    /// Because PoE quantities are integers, a result with excessive rounding discount is rejected.
    /// </summary>
    public static QuoteResult Calculate(
        int haveToSell,
        IEnumerable<MarketOrder> marketOrders,
        decimal maximumActualUndercutPercent = 2m)
    {
        if (haveToSell <= 0)
            return QuoteResult.Invalid("Enter a positive I HAVE quantity.");
        if (marketOrders == null)
            return QuoteResult.Invalid("Currency Exchange orders are unavailable.");
        if (maximumActualUndercutPercent < 1m || maximumActualUndercutPercent > 100m)
            return QuoteResult.Invalid("Invalid rounding safety limit.");

        var found = false;
        MarketOrder best = default;
        decimal bestPrice = decimal.MaxValue;

        foreach (var order in marketOrders)
        {
            if (order.Want <= 0 || order.Have <= 0 || order.ListedCount <= 0)
                continue;

            decimal price = (decimal)order.Want / order.Have;
            if (!found || price < bestPrice ||
                (price == bestPrice && order.ListedCount > best.ListedCount))
            {
                found = true;
                best = order;
                bestPrice = price;
            }
        }

        if (!found)
            return QuoteResult.Invalid("No usable competing offers. Open the currency pair at Faustus.");

        decimal fairWant = haveToSell * bestPrice;
        decimal target = decimal.Floor(fairWant * (1m - Discount));

        if (target < 1m)
            return QuoteResult.Invalid("The calculated I WANT amount is below 1. Sell a larger quantity.");
        if (target > int.MaxValue)
            return QuoteResult.Invalid("The calculated I WANT amount exceeds the game's integer limit.");

        int wanted = (int)target;
        decimal actual = (1m - wanted / fairWant) * 100m;
        if (actual > maximumActualUndercutPercent + 0.00000001m)
            return QuoteResult.Invalid(
                $"Rounding would undercut by {actual:0.##}% (limit {maximumActualUndercutPercent:0.##}%). Increase quantity.");

        int gcd = Gcd(haveToSell, wanted);
        return new QuoteResult(true, string.Empty, wanted, haveToSell,
            best, fairWant, actual, haveToSell / gcd);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int r = a % b;
            a = b;
            b = r;
        }
        return a;
    }
}
