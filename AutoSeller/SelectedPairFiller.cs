using System;
using System.Threading;
using System.Threading.Tasks;

namespace FaustusOnePercent.AutoSeller;

/// <summary>F7: maximum available HAVE, minimum integer tick below the best seller. Fills only.</summary>
public static class SelectedPairFiller
{
    public static async Task<SaleQuote> FillMaximum(IGameUiAdapter ui, SellerPolicy settings, CancellationToken token)
    {
        void Verify(FavoritePair expected = null)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = ui.Snapshot();
            if (!snapshot.PanelOpen || !snapshot.Foreground) throw new InvalidOperationException("Exchange closed or game lost focus");
            if (expected != null && snapshot.SelectedPair?.Key != expected.Key) throw new InvalidOperationException("WANT/HAVE pair changed");
        }
        Verify();
        var pair = ui.Snapshot().SelectedPair;
        if (pair == null || pair.Want.Metadata == pair.Have.Metadata) throw new InvalidOperationException("Select different I WANT and I HAVE currencies first");
        var stock = await ui.RefreshSelectedStock(pair, token);
        Verify(pair);
        if (!stock.Available.HasValue || (!stock.PickerOwned.HasValue && !stock.Complete))
            throw new InvalidOperationException("Maximum stock is unknown; refresh the HAVE currency picker");
        if (stock.Available.Value <= 0 || stock.Available.Value > int.MaxValue)
            throw new InvalidOperationException("Available quantity is zero or exceeds the game's integer limit");
        int quantity = (int)stock.Available.Value;
        // F7 is independent of queue mode, reserves and automatic placement preferences.
        var policy = settings with { Mode = PricingMode.BeatCompetitor, MinimumQuantity = 1, KeepReserve = 0, AutoPlace = false };
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Verify(pair);
            var quote = PricingEngine.Calculate(pair, quantity, ui.ReadMarket(pair), policy);
            if (!quote.Valid) throw new InvalidOperationException(quote.Error);
            await ui.Fill(pair, quote.Want, quote.Have, token);
            Verify(pair);
            var filled = ui.Snapshot();
            if (filled.WantInput != quote.Want || filled.HaveInput != quote.Have)
                throw new InvalidOperationException("Both numbers could not be verified");
            var fresh = PricingEngine.Calculate(pair, quantity, ui.ReadMarket(pair), policy);
            if (fresh.Valid && fresh.Want == quote.Want) return quote;
        }
        throw new InvalidOperationException("Market changed during filling; press F7 again to refresh");
    }
}
