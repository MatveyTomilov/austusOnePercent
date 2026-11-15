using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FaustusOnePercent.AutoSeller;

/// <summary>One sequential worker. An uncertain placement is latched across restarts by the journal.</summary>
public sealed class OrderExecutor
{
    private readonly IGameUiAdapter ui;
    private readonly Action<string> log;
    private readonly Action<string, FavoritePair, SaleQuote> journal;
    private readonly Action clearJournal;
    private readonly Func<bool> hasPending;
    private readonly object gate = new();
    private CancellationTokenSource cancellation;
    private List<SaleRow> rows = new();
    private volatile bool running;
    public bool Running => running;
    public ExecutorState State { get; private set; } = ExecutorState.IDLE;
    public string Status { get; private set; } = "Ready";
    public OrderExecutor(IGameUiAdapter adapter, Action<string> logger = null,
        Action<string, FavoritePair, SaleQuote> persist = null, Action clear = null, Func<bool> pending = null)
    { ui = adapter; log = logger ?? (_ => { }); journal = persist ?? ((_, _, _) => { }); clearJournal = clear ?? (() => { }); hasPending = pending ?? (() => false); }
    public IReadOnlyList<SaleRow> Rows { get { lock (gate) return rows.Select(r => new SaleRow { Pair = r.Pair, Rule = r.Rule, Stock = r.Stock, Quote = r.Quote, Status = r.Status }).ToArray(); } }
    private void Set(ExecutorState state, string message) { State = state; Status = message; log(state + ": " + message); }
    public void Stop() { cancellation?.Cancel(); Status = "Emergency stop requested"; }
    public Task Run(SellerPolicy policy, IReadOnlyList<PairRule> rules, bool prepare,
        IReadOnlyList<FavoritePair> explicitPairs = null)
    {
        lock (gate)
        {
            if (running) throw new InvalidOperationException("An operation is already running");
            if (hasPending()) throw new InvalidOperationException("Unconfirmed placement journal exists. Reconcile active orders before restarting.");
            running = true; cancellation = new();
        }
        return Execute(policy, rules.ToArray(), prepare, explicitPairs, cancellation.Token);
    }
    private void EnsureOpen(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var s = ui.Snapshot();
        if (!s.PanelOpen || !s.Foreground) throw new InvalidOperationException("Exchange closed or game lost focus");
    }
    private static bool Acknowledged(OrderInfo order, FavoritePair pair, SaleQuote quote, HashSet<int> before)
        => !before.Contains(order.Id) && !order.Canceled && order.WantMetadata == pair.Want.Metadata
            && order.HaveMetadata == pair.Have.Metadata && order.OriginalHave == quote.Have
            && order.RatioWant > 0 && order.RatioHave > 0
            && (long)order.RatioWant * quote.Have == (long)order.RatioHave * quote.Want;
    private async Task Execute(SellerPolicy policy, IReadOnlyList<PairRule> rules, bool prepare,
        IReadOnlyList<FavoritePair> explicitPairs, CancellationToken token)
    {
        bool clicked = false;
        try
        {
            EnsureOpen(token); Set(ExecutorState.READING_FAVORITES, "Reading Favorites");
            if (explicitPairs == null) await ui.OpenFavorites(token);
            var favorites = explicitPairs == null ? ui.ReadFavorites() : new FavoritesResult(explicitPairs, true, "Explicit current pair");
            if (!favorites.Verified && favorites.Pairs.Count > 0) throw new InvalidOperationException(favorites.Diagnostic);
            Set(ExecutorState.SCANNING_STASH, "Scanning confirmed inventory sources");
            var list = OrderQueue.Build(favorites.Pairs, rules, ui.ScanInventory, policy);
            lock (gate) rows = list;
            Set(ExecutorState.BUILDING_QUEUE, $"{list.Count} pairs");
            int processed = 0;
            foreach (var row in list)
            {
                if (row.Status != "Queued") continue;
                if (processed >= policy.MaximumOrders) { row.Status = "Session order limit"; continue; }
                EnsureOpen(token); Set(ExecutorState.SELECTING_PAIR, row.Pair.Key);
                await ui.SelectPair(row.Pair, token); await ui.WaitTick(token);
                EnsureOpen(token);
                Set(ExecutorState.READING_MARKET, row.Pair.Key);
                Set(ExecutorState.CALCULATING, "Calculating from verified in-game order book");
                int quantity = row.Quote.Have;
                var quote = PricingEngine.Calculate(row.Pair, quantity, ui.ReadMarket(row.Pair), policy, row.Rule.MinimumPrice);
                row.Quote = quote;
                if (!quote.Valid) { row.Status = quote.Error; continue; }
                row.Status = "Preview ready";
                if (!prepare) continue;
                var stock = ui.ScanInventory(row.Pair.Have);
                if (!stock.Available.HasValue || stock.Available.Value - row.Rule.KeepReserve < quote.Have)
                    throw new InvalidOperationException("Stock changed or cannot be corroborated; rescan inventory");
                var ids = ui.Snapshot().Orders.Select(o => o.Id).ToHashSet();
                Set(ExecutorState.FILLING, $"{quote.Want} WANT / {quote.Have} HAVE");
                await ui.Fill(row.Pair, quote.Want, quote.Have, token); await ui.WaitTick(token);
                Set(ExecutorState.VERIFYING, "Checking pair, numbers, slots, gold and fresh price");
                var s = ui.Snapshot();
                if (!s.PanelOpen || !s.Foreground || s.SelectedPair?.Key != row.Pair.Key || s.WantInput != quote.Want || s.HaveInput != quote.Have)
                    throw new InvalidOperationException("Pair or input verification failed");
                if (s.Orders.Count >= 10) throw new InvalidOperationException("All Faustus order slots are occupied");
                var fresh = PricingEngine.Calculate(row.Pair, quote.Have, ui.ReadMarket(row.Pair), policy, row.Rule.MinimumPrice);
                if (!fresh.Valid || fresh.Want != quote.Want) throw new InvalidOperationException("Market changed before placement; refresh preview");
                if (s.GoldCost.HasValue && s.Gold.HasValue && s.Gold.Value < s.GoldCost.Value)
                    throw new InvalidOperationException("Insufficient gold");
                if (policy.AutoPlace)
                {
                    if (!s.MappingVerified || !s.PlaceButtonActive || !s.GoldCost.HasValue || !s.Gold.HasValue)
                        throw new InvalidOperationException("Auto Place blocked: layout, place button or gold cost not verified");
                    // Persist BEFORE clicking. A crash after this point must not create a second order.
                    journal("PLACING", row.Pair, quote); clicked = true;
                    Set(ExecutorState.PLACING, "Submitting exactly once");
                    await ui.PlaceOnce(token);
                    Set(ExecutorState.CONFIRMING, "Waiting for a new matching order id");
                }
                else
                {
                    journal("WAITING_MANUAL", row.Pair, quote);
                    row.Status = "Prepared — review and click Place Order in game";
                    Set(ExecutorState.WAITING_MANUAL, row.Status); ui.AllowManualInput(true);
                }
                bool confirmed = false;
                int timeout = policy.AutoPlace ? 60 : 3000;
                for (int i = 0; i < timeout; i++)
                {
                    await ui.WaitTick(token); EnsureOpen(token);
                    var after = ui.Snapshot();
                    if (after.Orders.Any(o => Acknowledged(o, row.Pair, quote, ids))) { confirmed = true; break; }
                    if (after.SelectedPair?.Key != row.Pair.Key) throw new InvalidOperationException("Selected pair changed while waiting");
                }
                ui.AllowManualInput(false);
                if (!confirmed) throw new InvalidOperationException("Order not acknowledged. No automatic retry; reconcile active orders.");
                clearJournal(); clicked = false;
                row.Status = "Confirmed"; processed++;
                Set(ExecutorState.NEXT, "Order confirmed; next pair");
            }
            Set(ExecutorState.COMPLETE, prepare ? $"Confirmed {processed} order(s)" : "Preview complete; nothing submitted");
        }
        catch (OperationCanceledException) { Set(ExecutorState.STOPPED, clicked ? "Stopped after possible submission; reconcile active orders" : "Stopped"); }
        catch (Exception ex) { Set(ExecutorState.ERROR, ex.Message); }
        finally { ui.AllowManualInput(false); running = false; }
    }
}
