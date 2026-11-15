using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FaustusOnePercent.AutoSeller;

public sealed record SelectedSaleResult(SaleQuote Quote, int OrderId, bool SubmittedByPlugin);

/// <summary>One hotkey, one selected pair, one submission. An ambiguous result is never retried.</summary>
public static class SelectedPairSeller
{
    public static async Task<SelectedSaleResult> SellMaximum(IGameUiAdapter ui, SellerPolicy settings,
        Action<string, FavoritePair, SaleQuote> journal, Action clearJournal, Func<bool> hasPending,
        CancellationToken token)
    {
        if (hasPending()) throw new InvalidOperationException("Previous selected-pair order is unconfirmed. Reconcile active orders first.");
        var initial = ui.Snapshot();
        var pair = initial.SelectedPair;
        var before = initial.Orders.Select(o => o.Id).ToHashSet();
        var quote = await SelectedPairFiller.FillMaximum(ui, settings, token);
        token.ThrowIfCancellationRequested();
        var ready = ui.Snapshot();
        // A user may have clicked the prepared order before our final check. Acknowledge it,
        // rather than sending the same quantity a second time.
        var existing = ready.Orders.FirstOrDefault(o => OrderExecutor.Acknowledged(o, pair, quote, before));
        if (existing != null) return new(quote, existing.Id, false);
        if (!ready.PanelOpen || !ready.Foreground || ready.SelectedPair?.Key != pair.Key
            || ready.WantInput != quote.Want || ready.HaveInput != quote.Have)
            throw new InvalidOperationException("Selected pair or filled numbers changed before Place Order");
        if (ready.Orders.Count >= 10) throw new InvalidOperationException("All Faustus order slots are occupied");
        if (!ready.MappingVerified || !ready.PlaceButtonActive || !ready.Gold.HasValue || !ready.GoldCost.HasValue
            || ready.GoldCost < 0 || ready.GoldCost > ready.Gold)
            throw new InvalidOperationException("Place Order layout, gold cost or available gold could not be verified");
        var available = ui.ScanInventory(pair.Have).Available;
        if (!available.HasValue || available.Value < quote.Have) throw new InvalidOperationException("Available stock changed before Place Order");
        var policy = settings with { Mode = PricingMode.OneUnitBelowSeller, MinimumQuantity = 1, MaximumActualDiscount = 99 };
        var latest = PricingEngine.Calculate(pair, quote.Have, ui.ReadMarket(pair), policy);
        if (!latest.Valid || latest.Want != quote.Want) throw new InvalidOperationException("Seller price changed before Place Order; press the hotkey again");
        // Persist before the potentially irreversible input, including a failure inside PlaceOnce.
        journal("PLACING", pair, quote);
        await ui.PlaceOnce(token);
        for (int i = 0; i < 60; i++)
        {
            await ui.WaitTick(token);
            var after = ui.Snapshot();
            var confirmed = after.Orders.FirstOrDefault(o => OrderExecutor.Acknowledged(o, pair, quote, before));
            if (confirmed != null) { clearJournal(); return new(quote, confirmed.Id, true); }
            if (!after.PanelOpen || !after.Foreground || after.SelectedPair?.Key != pair.Key)
                throw new InvalidOperationException("UI changed after submission. Check active orders; no repeat submission.");
        }
        throw new InvalidOperationException("Order acknowledgement timed out. Check active orders; no repeat submission.");
    }
}
