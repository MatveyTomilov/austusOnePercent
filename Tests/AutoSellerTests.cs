using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FaustusOnePercent;
using FaustusOnePercent.AutoSeller;

internal static class AutoSellerTests
{
    static readonly Currency Chaos = new("chaos", "Chaos Orb"), Scour = new("scour", "Orb of Scouring"), Deck = new("deck", "Stacked Deck"), Divine = new("divine", "Divine Orb");
    static readonly FavoritePair Pair = new(Chaos, Scour, "exchange/favorites/0", true);
    static void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); Console.WriteLine("PASS: " + name); }
    static MarketBook Book(FavoritePair pair = null, int price = 1, bool verified = true) => new(pair ?? Pair, [new(price, 1, 100)], [new(1, 2, 100)], DateTimeOffset.UtcNow, verified, "test");
    static StockEvidence Stock(long value) => new(value, 0, null, 0, false, true, true, "test");
    static List<SaleRow> Queue(params FavoritePair[] pairs) => OrderQueue.Build(pairs, [], _ => Stock(100), new());
    static UiNode Node(string path, string name, float x, params UiNode[] children) => new(path, name, "", true, true, x, 10, 150, 30, children);
    public static async Task Run()
    {
        Check(Queue().Count == 0, "empty Favorites");
        Check(Queue(Pair).Single().Quote.Have == 100, "one Favorite");
        Check(Queue(Pair, new(Chaos, Deck, Verified: true)).Count == 2, "multiple Favorites");
        Check(Queue(Pair, new(Divine, Scour, Verified: true))[1].Quote.Have == 0, "shared HAVE cannot overspend local reservations");
        Check(OrderQueue.Build([Pair], [], _ => Stock(0), new()).Single().Status.StartsWith("Below"), "no currency");
        var partial = CurrencyInventoryScanner.Combine([new("same", true, 7), new("same", true, 7)], null, 0, false, true);
        Check(partial.Available == 7 && partial.KnownStash == 7 && !partial.Complete, "partial stash is lower bound; locations deduplicated");
        Check(CurrencyInventoryScanner.Combine([], null, 0, false, true).Available == null, "unloaded stash is unknown not zero");
        Check(new StockEvidence(100, 0, null, 20, true, true, true, "").Available == 80, "gross balance excludes escrow once");
        Check(new StockEvidence(80, 0, 80, 20, false, true, true, "").Available == 80, "picker available balance does not subtract escrow twice");
        var rowTree = Node("exchange", "", 0, Node("exchange/favorites", "", 0,
            Node("exchange/favorites/0", "", 0, Node("exchange/favorites/0/0", Chaos.Name, 10), Node("exchange/favorites/0/1", Scour.Name, 200))));
        var found = FavoritesReader.Read(rowTree, [Chaos, Scour], "exchange/favorites", true);
        Check(found.Verified && found.Pairs.Single().Key == Pair.Key, "Favorites resolves identities and column direction");
        Check(!FavoritesReader.Read(rowTree, [Chaos, Scour], "exchange/favorites", false).Verified, "unverified Favorites direction blocks execution");
        var beat = PricingEngine.Calculate(Pair, 100, Book(), new());
        Check(beat.Valid && beat.Want == 99, "minimum integer tick beats same-direction seller");
        Check(PricingEngine.Calculate(Pair, 100, Book(), new(Mode: PricingMode.MatchCompetitor)).Want == 100, "match mode");
        Check(PricingEngine.Calculate(Pair, 100, Book(), new(Mode: PricingMode.InstantExchange)).Want == 50, "instant mode uses bids only");
        Check(!PricingEngine.Calculate(Pair, 1, Book(), new()).Valid, "small lot cannot sell for zero");
        Check(!PricingEngine.Calculate(Pair, 10, Book(), new()).Valid, "excessive rounding rejected");
        Check(!PricingEngine.Calculate(Pair, 100, Book(new(Scour, Chaos, Verified:true)), new()).Valid, "wrong pair rejected");
        Check(!PricingEngine.Calculate(Pair, 100, Book(verified:false), new()).Valid, "unverified market orientation rejected");
        Check(!PricingEngine.Calculate(Pair, 100, Book() with { ReadAt=DateTimeOffset.UtcNow.AddMinutes(-1) }, new()).Valid, "stale book rejected");
        Check(!PricingEngine.Calculate(Pair, 100, Book(price:0), new()).Valid, "invalid market ratio rejected");
        Check(!PricingEngine.Calculate(Pair, 100, Book(), new(), 1).Valid, "minimum price floor enforced");
        var normalized = MarketReader.Normalize(Pair, [new(2,1,50)], [new(3,4,80)], MarketFeedMapping.WantedStockIsCompetitors, DateTimeOffset.UtcNow);
        Check(normalized.Competitors[0].Want == 2 && normalized.CounterOrders[0].Want == 4, "ask/bid orientation differs explicitly");
        var liveBook = MarketReader.Normalize(Pair, [new(50,59,403)], [new(20,17,120)], MarketFeedMapping.OfferedStockIsCompetitors, DateTimeOffset.UtcNow);
        var liveQuote = PricingEngine.Calculate(Pair, 99, liveBook, new());
        Check(liveQuote.Valid && liveQuote.Want == 84 && liveQuote.ReferencePrice == .85m, "live Scouring feed: ask 17/20, bid 50/59; 99 Scour priced at 84 Chaos");
        var repeating = Book() with { Competitors = [new(9,14,100)] };
        var exactTick = PricingEngine.Calculate(Pair, 28, repeating, new(MaximumActualDiscount:99));
        Check(exactTick.Valid && exactTick.Want == 17 && exactTick.ActualDiscount > 0,
            "exact rational tick: 28 units at 9/14 ask must beat 18, not match it");
        Check(!PricingEngine.Calculate(Pair, 28, repeating, new()).Valid,
            "queue still enforces the configured percentage cap after exact rounding fix");
        var fake = new Fake(); var executor = new OrderExecutor(fake);
        await executor.Run(new(), [], false);
        Check(executor.State == ExecutorState.COMPLETE && fake.Places == 0 && fake.Fills == 0, "Preview never fills or submits");
        fake = new() { Gold=0 }; executor = new(fake); await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.ERROR && fake.Places == 0, "no gold blocks placement");
        fake = new() { SlotsFull=true }; executor = new(fake); await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.ERROR && fake.Places == 0, "all ten order slots occupied");
        fake = new() { ChangeMarket=true }; executor = new(fake); await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.ERROR && fake.Places == 0, "market change before send aborts");
        fake = new() { Acknowledge=true, PartialFill=true }; executor = new(fake); await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.COMPLETE && fake.Places == 1, "partial fill acknowledged by original amount and new order id");
        bool pending = false; fake = new(); executor = new(fake, persist:(_,_,_) => pending=true, clear:()=>pending=false, pending:()=>pending);
        await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.ERROR && pending && fake.Places == 1, "ack failure latches uncertain order without retry");
        bool blocked=false; try { await executor.Run(new(AutoPlace:true), [], true); } catch(InvalidOperationException) { blocked=true; }
        Check(blocked && fake.Places == 1, "restart after ambiguous result cannot duplicate submission");
        fake = new(); executor = new(fake); fake.OnWait = () => executor.Stop(); await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.STOPPED && fake.Places == 0, "emergency stop before placement");
        fake = new(); executor = new(fake); fake.OnFill = () => fake.Open=false; await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.ERROR && fake.Places == 0, "closing exchange during fill aborts");
        fake = new() { WrongInputs=true }; executor = new(fake); await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.ERROR && fake.Places == 0, "both numeric fields verified");
        fake = new() { Layout=false }; executor = new(fake); await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.ERROR && fake.Places == 0, "unverified gold/button layout blocks Auto Place");
        fake = new() { Acknowledge=true }; executor = new(fake); fake.Favorites=[Pair,new(Chaos,Deck,"row2",true)];
        await executor.Run(new(AutoPlace:true), [], true);
        Check(executor.State == ExecutorState.COMPLETE && fake.Places == 2, "two favorites submitted sequentially and acknowledged independently");
        fake = new() { Acknowledge=true }; executor = new(fake); fake.OnWait = () => { if(executor.State==ExecutorState.WAITING_MANUAL) fake.Manual=true; };
        await executor.Run(new(), [], true);
        Check(executor.State == ExecutorState.COMPLETE && fake.Places == 0, "manual mode waits for user-created matching order");
        fake = new();
        var filledMax = await SelectedPairFiller.FillMaximum(fake, new(Mode:PricingMode.InstantExchange, AutoPlace:true, KeepReserve:99), CancellationToken.None);
        Check(filledMax.Have == 100 && filledMax.Want == 99 && fake.Places == 0 && fake.Fills == 1,
            "F7 fills maximum and beats sellers even when the queue uses bids/auto/reserves; never submits");
        fake = new() { StockValue = new(5,0,null,0,false,false,true,"partial") };
        bool stockBlocked = false;
        try { await SelectedPairFiller.FillMaximum(fake, new(), CancellationToken.None); } catch(InvalidOperationException) { stockBlocked = true; }
        Check(stockBlocked && fake.Fills == 0 && fake.Places == 0, "F7 refuses an incomplete lower bound as maximum stock");
        fake = new() { StockValue = new(0,0,0,0,false,true,true,"zero") };
        stockBlocked = false;
        try { await SelectedPairFiller.FillMaximum(fake, new(), CancellationToken.None); } catch(InvalidOperationException) { stockBlocked = true; }
        Check(stockBlocked && fake.Fills == 0, "F7 refuses zero stock");
        fake = new() { WrongInputs = true };
        bool inputBlocked = false;
        try { await SelectedPairFiller.FillMaximum(fake, new(), CancellationToken.None); } catch(InvalidOperationException) { inputBlocked = true; }
        Check(inputBlocked && fake.Places == 0, "F7 verifies both numbers and never clicks Place Order on failure");
        fake = new(); fake.OnFill = () => fake.Selected = new(Scour, Chaos, Verified:true);
        inputBlocked = false;
        try { await SelectedPairFiller.FillMaximum(fake, new(), CancellationToken.None); } catch(InvalidOperationException) { inputBlocked = true; }
        Check(inputBlocked && fake.Places == 0, "F7 aborts when user changes the pair");
        fake = new() { ChangeMarket=true };
        filledMax = await SelectedPairFiller.FillMaximum(fake, new(), CancellationToken.None);
        Check(filledMax.Want == 199 && fake.Fills == 2 && fake.Places == 0, "F7 refreshes a changed seller price once before returning");
        fake = new() { StockValue = Stock(9) };
        filledMax = await SelectedPairFiller.FillMaximum(fake, new(MaximumActualDiscount:2), CancellationToken.None);
        Check(filledMax.Have == 9 && filledMax.Want == 8 && filledMax.ActualDiscount > 11 && fake.Places == 0,
            "F7 allows the explicit minimum tick on small essence lots despite a 2% queue cap");
        fake = new() { StockValue = Stock(1) }; stockBlocked = false;
        try { await SelectedPairFiller.FillMaximum(fake, new(), CancellationToken.None); } catch(InvalidOperationException) { stockBlocked = true; }
        Check(stockBlocked && fake.Fills == 0 && fake.Places == 0, "F7 still refuses a zero-return quote after lifting its percentage cap");
        var fractional = Book() with { Competitors = [new(59,10,100)] };
        var flatOne = PricingEngine.Calculate(Pair, 5, fractional, new(Mode:PricingMode.OneUnitBelowSeller, MaximumActualDiscount:99));
        Check(flatOne.Valid && flatOne.Want == 28 && flatOne.Have == 5,
            "flat discount: floor(5 essences at 5.9 Chaos) minus 1 = 28 Chaos, not 29");
        fake = new() { Selected = new(Divine, Scour, Verified:true) }; inputBlocked = false;
        try { await SelectedPairFiller.FillMaximum(fake, new(), CancellationToken.None); } catch(InvalidOperationException) { inputBlocked = true; }
        Check(inputBlocked && fake.Fills == 0 && fake.Places == 0, "F7 never mistakes one Divine for a one-Chaos discount");
    }
    sealed class Fake : IGameUiAdapter
    {
        public IReadOnlyList<FavoritePair> Favorites=[Pair];
        public FavoritePair Selected=Pair;
        public bool Open=true, SlotsFull, ChangeMarket, Acknowledge, PartialFill, WrongInputs, Layout=true, Manual;
        public long Gold=10000; public int Places,Fills,Reads,Want,Have;
        public StockEvidence StockValue = Stock(100);
        public Action OnWait,OnFill;
        readonly List<OrderInfo> orders=new();
        public FavoritesResult ReadFavorites()=>new(Favorites,true,"");
        public StockEvidence ScanInventory(Currency currency)=>StockValue;
        public Task SelectPair(FavoritePair pair,CancellationToken token){token.ThrowIfCancellationRequested();Selected=pair;return Task.CompletedTask;}
        public MarketBook ReadMarket(FavoritePair pair)=>Book(pair, ChangeMarket && ++Reads>1 ? 2 : 1);
        public Task Fill(FavoritePair pair,int want,int have,CancellationToken token){token.ThrowIfCancellationRequested(); Fills++;Want=want;Have=have;OnFill?.Invoke();return Task.CompletedTask;}
        public ExecutionSnapshot Snapshot()
        {
            if((Places>orders.Count || Manual) && Acknowledge && !orders.Any(o=>o.HaveMetadata==Selected.Have.Metadata))
                orders.Add(new(100+orders.Count,Selected.Want.Metadata,Selected.Have.Metadata,Have,PartialFill?10:Have,Want,Have,false,false,100));
            var list= SlotsFull ? Enumerable.Range(1,10).Select(i=>new OrderInfo(i,"x","y",1,1,1,1,false,false,100)).ToArray() : orders.ToArray();
            return new(Open,true,Selected,WrongInputs?0:Want,Have,Gold,100,list,true,Layout);
        }
        public Task PlaceOnce(CancellationToken token){token.ThrowIfCancellationRequested();Places++;return Task.CompletedTask;}
        public Task WaitTick(CancellationToken token){OnWait?.Invoke();token.ThrowIfCancellationRequested();return Task.CompletedTask;}
        public void AllowManualInput(bool allowed){}
    }
}
