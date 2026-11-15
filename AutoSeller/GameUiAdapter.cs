using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.Village;
using FaustusOnePercent.Utils;

namespace FaustusOnePercent.AutoSeller;

public sealed class GameUiAdapter : IGameUiAdapter
{
    private readonly GameController game;
    private readonly AutoSellerSettings settings;
    private CurrencyExchangePanel discovered;
    private DateTime nextDiscovery;
    private readonly ConcurrentDictionary<string, (long Quantity, DateTimeOffset At, HashSet<int> Orders)> ownedCache = new();
    private (FavoritePair Pair, int Want, int Have)? prepared;
    public Element WantInput => Panel?.GetChildAtIndex(5);
    public Element HaveInput => Panel?.GetChildAtIndex(8);
    public Element PickerElement => Panel?.GetChildAtIndex(20);
    public GameUiAdapter(GameController controller, AutoSellerSettings options) { game = controller; settings = options; }
    private static string Text(Element e) => string.IsNullOrWhiteSpace(e?.TextNoTags) ? e?.Text ?? "" : e.TextNoTags;
    private static bool LooksLikeExchange(Element e) => e != null
        && e.Children.Any(c => Text(c).Equals("I Have", StringComparison.OrdinalIgnoreCase))
        && e.Children.Any(c => c.Children.Any(t => Text(t).Equals("place order", StringComparison.OrdinalIgnoreCase)));
    public CurrencyExchangePanel Panel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(settings.ExchangePanelPath))
            {
                var bound = ResolveRoot(settings.ExchangePanelPath);
                if (LooksLikeExchange(bound)) return bound.AsObject<CurrencyExchangePanel>();
            }
            var provided = game.IngameState?.IngameUi?.CurrencyExchangePanel;
            if (LooksLikeExchange(provided)) return provided;
            if (LooksLikeExchange(discovered)) return discovered;
            if (DateTime.UtcNow >= nextDiscovery)
            {
                nextDiscovery = DateTime.UtcNow.AddSeconds(2);
                discovered = game.IngameState?.IngameUi?.Children.FirstOrDefault(LooksLikeExchange)?.AsObject<CurrencyExchangePanel>();
            }
            return discovered ?? provided;
        }
    }
    public IReadOnlyList<Currency> Catalog => game.Files.CurrencyExchange.EntriesList
        .Where(e => e.BaseItemType != null).Select(e => new Currency(e.BaseItemType.Metadata, e.BaseItemType.BaseName))
        .GroupBy(c => c.Metadata).Select(g => g.First()).ToArray();
    public FavoritePair CurrentPair()
    {
        var p = Panel;
        return p?.WantedItemType == null || p.OfferedItemType == null ? null : new(
            new(p.WantedItemType.Metadata, p.WantedItemType.BaseName), new(p.OfferedItemType.Metadata, p.OfferedItemType.BaseName), Verified: true);
    }
    public UiNode CaptureTree(bool full = false)
    {
        var seen = new HashSet<long>(); int budget = full ? 25000 : 6000;
        UiNode Visit(Element e, string path, int depth)
        {
            if (e == null || e.Address == 0 || depth > 16 || budget-- <= 0 || !seen.Add(e.Address)) return null;
            try
            {
                var rect = e.GetClientRectCache;
                string metadata = ""; try { metadata = e.Entity?.Path ?? ""; } catch { }
                var children = new List<UiNode>();
                var source = full && depth > 0 && !e.IsVisible ? Array.Empty<Element>() : e.Children;
                for (int i = 0; i < source.Count && budget > 0; i++)
                { var child = Visit(source[i], path + "/" + i, depth + 1); if (child != null) children.Add(child); }
                string text = e.TextNoTags;
                if (string.IsNullOrEmpty(text)) text = e.Text;
                return new(path, text ?? "", metadata, e.IsVisible, e.IsActive,
                    rect.X, rect.Y, rect.Width, rect.Height, children, e.IsVisibleLocal);
            }
            catch { return new(path, "<unreadable>", "", false, false, 0, 0, 0, 0, Array.Empty<UiNode>()); }
        }
        return Visit(full ? game.IngameState.IngameUi : Panel, full ? "ui" : "exchange", 0);
    }
    public Element Resolve(string path)
    {
        var parts = path?.Split('/') ?? Array.Empty<string>();
        if (parts.Length > 0 && parts[0] == "ui") return ResolveRoot(path);
        if (parts.Length == 0 || parts[0] != "exchange") return null;
        Element node = Panel;
        foreach (var part in parts.Skip(1))
        { if (!int.TryParse(part, out int i) || node == null || i < 0 || i >= node.Children.Count) return null; node = node.Children[i]; }
        return node;
    }
    private Element ResolveRoot(string path)
    {
        Element node = game.IngameState?.IngameUi;
        foreach (var part in path.Split('/').Skip(1))
        { if (!int.TryParse(part, out int i) || node == null || i < 0 || i >= node.Children.Count) return null; node = node.Children[i]; }
        return node;
    }
    public FavoritesResult ReadFavorites()
    {
        var tree = CaptureTree();
        CachePickerStock(tree);
        return FavoritesReader.Read(tree, Catalog, settings.FavoritesRootPath, settings.FavoritesWantOnLeftVerified);
    }
    private void CachePickerStock(UiNode tree)
    {
        if (PickerElement?.IsVisibleLocal != true || !Text(PickerElement.GetChildAtIndex(3)).Equals("I Have", StringComparison.OrdinalIgnoreCase)) return;
        var ids = Orders().Select(o => o.Id).ToHashSet();
        foreach (var item in CurrencyInventoryScanner.ReadPickerOwned(tree, Catalog))
            ownedCache[item.Key] = (item.Value, DateTimeOffset.UtcNow, ids);
    }
    public StockEvidence ScanInventory(Currency currency)
    {
        var locations = new List<InventoryLocation>(); var seen = new HashSet<long>();
        foreach (var holder in game.IngameState.ServerData.PlayerInventories)
        {
            var inv = holder.Inventory;
            if (inv == null || !seen.Add(inv.Address)) continue;
            bool main = inv.InventType.ToString() == "MainInventory";
            bool stash = inv.InventSlot.ToString() == "StashInventoryId";
            if (!main && !stash) continue;
            foreach (var item in inv.Items)
                if (item?.Path == currency.Metadata)
                    locations.Add(new(item.Address.ToString(), stash, item.GetComponent<Stack>()?.Size ?? 1));
        }
        long? owned = null;
        // A stale picker from the opposite column must never corroborate our HAVE stock.
        var orders = Orders();
        if (ownedCache.TryGetValue(currency.Metadata, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(2))
            owned = Math.Max(0, cached.Quantity - orders.Where(o => !cached.Orders.Contains(o.Id) && o.HaveMetadata == currency.Metadata && !o.Canceled).Sum(o => (long)o.OriginalHave));
        long escrow = orders.Where(o => o.HaveMetadata == currency.Metadata && !o.Complete && !o.Canceled).Sum(o => (long)o.RemainingHave);
        return CurrencyInventoryScanner.Combine(locations, owned, escrow, false, true);
    }
    public async Task<StockEvidence> RefreshSelectedStock(FavoritePair pair, CancellationToken token)
    {
        Guard(token, pair);
        if (PickerElement?.IsVisibleLocal == true) throw new InvalidOperationException("Finish selecting currencies, close the picker, then press Mouse5");
        await OpenFavorites(token);
        Guard(token, pair);
        var visibleCounts = CurrencyInventoryScanner.ReadPickerOwned(CaptureTree(), Catalog);
        long? count = visibleCounts.TryGetValue(pair.Have.Metadata, out long shown) ? shown : null;
        // Prefer the structured on-screen count already verified in this host. The
        // public Owned API is a fallback for options outside the bounded UI snapshot.
        try
        {
            if (!count.HasValue)
            {
                var picker = PickerElement.AsObject<CurrencyExchangeCurrencyPickerElement>();
                var option = picker.Options.FirstOrDefault(o => o.ItemType?.Metadata == pair.Have.Metadata);
                if (option != null && option.Owned >= 0) count = option.Owned;
            }
        }
        catch { /* The text reader remains a validated source if this host lacks working option offsets. */ }
        if (!count.HasValue) throw new InvalidOperationException("No confirmed owned count for the selected HAVE currency");
        ownedCache[pair.Have.Metadata] = (count.Value, DateTimeOffset.UtcNow, Orders().Select(o => o.Id).ToHashSet());
        await Click(PickerElement.GetChildAtIndex(2), token, () => Guard(token, pair));
        for (int i = 0; i < 20; i++)
        {
            await WaitTick(token); Guard(token, pair);
            if (PickerElement?.IsVisibleLocal != true)
            { await Task.Delay(300, token); Guard(token, pair); return ScanInventory(pair.Have); }
        }
        throw new InvalidOperationException("Currency picker did not close");
    }
    public MarketBook ReadMarket(FavoritePair pair)
    {
        if (CurrentPair()?.Key != pair.Key) return new(pair, Array.Empty<MarketOrder>(), Array.Empty<MarketOrder>(), DateTimeOffset.UtcNow, false, "Selected pair changed");
        var p = Panel;
        return MarketReader.Normalize(pair,
            p.WantedItemStock.Select(x => new MarketOrder(x.Get, x.Give, x.ListedCount)),
            p.OfferedItemStock.Select(x => new MarketOrder(x.Get, x.Give, x.ListedCount)), settings.MarketMapping, DateTimeOffset.UtcNow);
    }
    public IReadOnlyList<OrderInfo> Orders() => game.IngameState.ServerData.PlacedCurrencyExchangeOrders.Select(o => new OrderInfo(
        o.PlayerOrderId, o.WantedItemType?.Metadata ?? "", o.OfferedItemType?.Metadata ?? "", o.OriginalOfferedItemStackSize,
        o.OfferedItemStackSize, o.WantedItemRatioPart, o.OfferedItemRatioPart, o.IsCompleted, o.IsCanceled, o.GoldCost)).ToArray();
    public static int? ReadInteger(Element element)
    {
        if (element == null) return null;
        string text = Text(element);
        if (string.IsNullOrWhiteSpace(text)) text = element.FindChildRecursive(e => !string.IsNullOrWhiteSpace(e.TextNoTags))?.TextNoTags;
        if (string.IsNullOrWhiteSpace(text) || !Regex.IsMatch(text.Trim(), @"^\d[\d\s,\.]*$")) return null;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int number) ? number : null;
    }
    private int? ReadGoldCost()
    {
        var e = Resolve(settings.GoldCostPath); var text = Text(e);
        var match = Regex.Match(text, @"^<<village_gold>>\s*<goldvalue>\{([\d\s,\.]+)\}$");
        if (match.Success) return int.TryParse(new string(match.Groups[1].Value.Where(char.IsDigit).ToArray()), out int cost) ? cost : null;
        return ReadInteger(e);
    }
    public ExecutionSnapshot Snapshot()
    {
        var p = Panel; var place = Resolve(settings.PlaceButtonPath);
        return new(p?.IsVisible == true, game.Window.IsForeground(), CurrentPair(), ReadInteger(WantInput),
            ReadInteger(HaveInput), game.IngameState?.ServerData?.Gold, ReadGoldCost(), Orders(),
            place?.IsVisible == true && place.IsActive, settings.PlacementLayoutVerified);
    }
    public async Task SelectPair(FavoritePair pair, CancellationToken token)
    {
        Guard(token);
        if (PickerElement?.IsVisibleLocal == true && !Text(PickerElement.GetChildAtIndex(3)).Equals("I Have", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Close the WANT picker first; refusing to reinterpret its rows as HAVE");
        if (string.IsNullOrEmpty(pair.UiPath))
        { if (CurrentPair()?.Key == pair.Key && PickerElement?.IsVisibleLocal != true) return; throw new InvalidOperationException("Favorite has no validated UI row; scan visible Favorites again"); }
        await OpenFavorites(token);
        var live = ReadFavorites().Pairs.FirstOrDefault(p => p.Key == pair.Key && p.Verified);
        if (live == null) throw new InvalidOperationException("Favorite disappeared or changed direction");
        await Click(Resolve(live.UiPath + "/2"), token);
        for (int i = 0; i < 20; i++) { await WaitTick(token); Guard(token); if (CurrentPair()?.Key == pair.Key && PickerElement?.IsVisibleLocal != true) return; }
        throw new InvalidOperationException("Favorite selection did not produce the expected WANT/HAVE pair");
    }
    public async Task OpenFavorites(CancellationToken token)
    {
        Guard(token);
        if (PickerElement?.IsVisibleLocal != true) await Click(Panel.GetChildAtIndex(10), token);
        for (int i = 0; i < 20; i++)
        {
            Guard(token);
            if (PickerElement?.IsVisibleLocal == true && Text(PickerElement.GetChildAtIndex(3)).Equals("I Have", StringComparison.OrdinalIgnoreCase)) return;
            await WaitTick(token);
        }
        throw new InvalidOperationException("HAVE currency picker did not open");
    }
    private void Guard(CancellationToken token, FavoritePair pair = null)
    {
        token.ThrowIfCancellationRequested();
        if (Panel?.IsVisible != true || !game.Window.IsForeground()) throw new InvalidOperationException("Exchange closed or game lost focus");
        if (pair != null && CurrentPair()?.Key != pair.Key) throw new InvalidOperationException("WANT/HAVE pair changed");
    }
    private async Task<Vector2> Click(Element e, CancellationToken token, Action verify = null)
    {
        Guard(token);
        if (e?.IsVisible != true) throw new InvalidOperationException("Target element not visible");
        var r = e.GetClientRectCache;
        if (r.Width < 1 || r.Height < 1) throw new InvalidOperationException("Target has invalid bounds");
        var origin = game.Window.GetWindowRectangleTimeCache.TopLeft;
        var point = new Vector2(r.Center.X + origin.X, r.Center.Y + origin.Y);
        Mouse.SetPosition(point); await Task.Delay(60, token); Guard(token); CheckMouse(point); verify?.Invoke();
        await Mouse.LeftDown(); await Mouse.LeftUp(); await Task.Delay(100, token);
        return point;
    }
    public async Task Fill(FavoritePair pair, int want, int have, CancellationToken token)
    {
        async Task Field(Element element, int value)
        {
            Guard(token, pair); var point = await Click(element, token);
            // Only send digits to a positively focused input, never to a guessed field.
            var focused = game.IngameState.FocusedInputElement;
            if (focused == null || (focused.Address != element.Address && !IsDescendant(focused, element)))
                throw new InvalidOperationException("Expected exchange input did not receive focus");
            void CheckField()
            {
                Guard(token, pair); CheckMouse(point);
                var input = game.IngameState.FocusedInputElement;
                if (input == null || !IsDescendant(input, element)) throw new InvalidOperationException("Input focus changed during typing");
            }
            for (int i = 0; i < 12; i++) { CheckField(); await Keyboard.KeyPress(Keys.Back); }
            foreach (char digit in value.ToString()) { CheckField(); await Keyboard.Type(digit.ToString()); }
            for (int i = 0; i < 20; i++) { Guard(token, pair); if (ReadInteger(element) == value) return; await WaitTick(token); }
            throw new InvalidOperationException("Input verification failed");
        }
        await Field(HaveInput, have);
        await Field(WantInput, want);
        Guard(token, pair);
        prepared = (pair, want, have);
    }
    private static bool IsDescendant(Element child, Element parent)
    { for (int i = 0; child != null && i < 12; i++, child = child.Parent) if (child.Address == parent.Address) return true; return false; }
    private static void CheckMouse(Vector2 expected)
    { var p = Mouse.GetCursorPosition(); if (Vector2.Distance(new(p.X, p.Y), expected) > 12) throw new OperationCanceledException("Mouse moved"); }
    public async Task PlaceOnce(CancellationToken token)
    {
        var expected = prepared ?? throw new InvalidOperationException("No verified prepared order");
        void Verify()
        {
            Guard(token, expected.Pair); var s = Snapshot();
            if (!s.MappingVerified || s.WantInput != expected.Want || s.HaveInput != expected.Have || s.Orders.Count >= 10
                || !s.GoldCost.HasValue || !s.Gold.HasValue || s.GoldCost < 0 || s.GoldCost > s.Gold
                || !s.PlaceButtonActive || !Text(Resolve(settings.PlaceButtonPath)).Equals("place order", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Final placement guard failed");
        }
        Verify(); await Click(Resolve(settings.PlaceButtonPath), token, Verify); prepared = null;
    }
    public Task WaitTick(CancellationToken token) => Task.Delay(100, token);
    public void AllowManualInput(bool allowed) { }
}
