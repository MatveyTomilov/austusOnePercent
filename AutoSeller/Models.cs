using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FaustusOnePercent.AutoSeller;

public sealed record Currency(string Metadata, string Name);
public sealed record FavoritePair(Currency Want, Currency Have, string UiPath = "", bool Verified = false)
{
    public string Key => Want.Metadata + " <- " + Have.Metadata;
}
public enum PricingMode { BeatCompetitor, MatchCompetitor, Undercut, InstantExchange, OneUnitBelowSeller }
public enum ExecutorState { IDLE, READING_FAVORITES, SCANNING_STASH, BUILDING_QUEUE, SELECTING_PAIR,
    READING_MARKET, CALCULATING, FILLING, VERIFYING, PLACING, CONFIRMING, WAITING_MANUAL, NEXT, STOPPED, ERROR, COMPLETE }
public sealed class PairRule
{
    public string WantMetadata { get; set; } = "";
    public string HaveMetadata { get; set; } = "";
    public int MinimumQuantity { get; set; } = 1;
    public int KeepReserve { get; set; }
    public int MaximumQuantity { get; set; }
    public decimal MinimumPrice { get; set; }
}
public sealed record SellerPolicy(PricingMode Mode = PricingMode.BeatCompetitor, decimal UndercutPercent = 1,
    decimal MaximumActualDiscount = 2, int MinimumQuantity = 1, int KeepReserve = 0, int MaximumOrders = 5,
    bool AutoPlace = false, int MaximumQuoteAgeSeconds = 3);
public sealed record StockEvidence(long Inventory, long KnownStash, long? PickerOwned,
    long ReservedInOrders, bool TotalsIncludeReserved, bool Complete, bool HasSnapshot, string Diagnostic)
{
    // Normal inventory/picker balances already exclude currency removed into orders.
    // ReservedInOrders is informational unless a provider explicitly returns gross balances.
    public long? Available
    {
        get
        {
            if (!HasSnapshot && !PickerOwned.HasValue) return null;
            var known = Math.Max(0, Inventory) + Math.Max(0, KnownStash);
            long observed = PickerOwned ?? known;
            if (!PickerOwned.HasValue && !Complete && observed == 0) return null;
            return Math.Max(0, observed - (TotalsIncludeReserved ? ReservedInOrders : 0));
        }
    }
}
public sealed record MarketBook(FavoritePair Pair, IReadOnlyList<MarketOrder> Competitors,
    IReadOnlyList<MarketOrder> CounterOrders, DateTimeOffset ReadAt, bool DirectionVerified, string Diagnostic);
public sealed record SaleQuote(bool Valid, string Error, int Want, int Have, decimal ReferencePrice,
    decimal SalePrice, decimal ActualDiscount, int MinimumLot)
{
    public static SaleQuote Invalid(string error) => new(false, error, 0, 0, 0, 0, 0, 0);
}
public sealed record OrderInfo(int Id, string WantMetadata, string HaveMetadata, int OriginalHave,
    int RemainingHave, int RatioWant, int RatioHave, bool Complete, bool Canceled, int GoldCost);
public sealed record ExecutionSnapshot(bool PanelOpen, bool Foreground, FavoritePair SelectedPair,
    int? WantInput, int? HaveInput, long? Gold, long? GoldCost, IReadOnlyList<OrderInfo> Orders,
    bool PlaceButtonActive, bool MappingVerified);
public sealed class SaleRow
{
    public FavoritePair Pair { get; init; }
    public PairRule Rule { get; init; }
    public StockEvidence Stock { get; set; }
    public SaleQuote Quote { get; set; } = SaleQuote.Invalid("Not priced");
    public string Status { get; set; } = "Queued";
}
public sealed record UiNode(string Path, string Text, string Metadata, bool Visible, bool Active,
    float X, float Y, float Width, float Height, IReadOnlyList<UiNode> Children, bool LocalVisible = true)
{
    public IEnumerable<UiNode> Walk() { yield return this; foreach (var c in Children) foreach (var n in c.Walk()) yield return n; }
}
public sealed record FavoritesResult(IReadOnlyList<FavoritePair> Pairs, bool Verified, string Diagnostic);
public interface IGameUiAdapter
{
    Task OpenFavorites(CancellationToken token) => Task.CompletedTask;
    FavoritesResult ReadFavorites();
    StockEvidence ScanInventory(Currency currency);
    Task<StockEvidence> RefreshSelectedStock(FavoritePair pair, CancellationToken token)
        => Task.FromResult(ScanInventory(pair.Have));
    Task SelectPair(FavoritePair pair, CancellationToken token);
    MarketBook ReadMarket(FavoritePair pair);
    Task Fill(FavoritePair pair, int want, int have, CancellationToken token);
    ExecutionSnapshot Snapshot();
    Task PlaceOnce(CancellationToken token);
    Task WaitTick(CancellationToken token);
    void AllowManualInput(bool allowed);
}
