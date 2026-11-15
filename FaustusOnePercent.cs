using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ExileCore;
using FaustusOnePercent.AutoSeller;
using Hexa.NET.ImGui;
using Newtonsoft.Json;

namespace FaustusOnePercent;

public sealed class FaustusOnePercent : BaseSettingsPlugin<FaustusOnePercentSettings>
{
    private AutoSellerSettings options;
    private GameUiAdapter adapter;
    private OrderExecutor executor;
    private Diagnostics diagnostics;
    private string configPath;
    private FavoritesResult favorites = new(Array.Empty<FavoritePair>(), false, "Not scanned");
    private string status = "Ready";
    private string favoritesPath = "", goldPath = "", placePath = "";
    private string inspectorFilter = "";
    private UiNode inspector;
    private int manualQuantity = 100;
    private bool filling;
    private DateTime nextRequestCheck;
    private DateTime configWritten;
    private CancellationTokenSource manualAction = new();
    public override bool Initialise()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "config", "FaustusAutoSeller");
        diagnostics = new(directory); configPath = Path.Combine(directory, "settings.json");
        try { options = File.Exists(configPath) ? JsonConvert.DeserializeObject<AutoSellerSettings>(File.ReadAllText(configPath)) : new(); }
        catch (Exception ex) { options = new(); LogError("AutoSeller config: " + ex.Message); }
        options ??= new(); options.AutoPlaceOrders = false;
        favoritesPath = options.FavoritesRootPath; goldPath = options.GoldCostPath; placePath = options.PlaceButtonPath;
        adapter = new(GameController, options);
        executor = new(adapter, diagnostics.Log, diagnostics.Journal, diagnostics.ClearPending, () => diagnostics.HasPending);
        foreach (var key in new[] { Settings.ScanKey, Settings.PreviewKey, Settings.StartKey, Settings.StopKey, Settings.AutoKey })
        { Input.RegisterKey(key.Value); key.OnValueChanged += () => Input.RegisterKey(key.Value); }
        LogMessage("Faustus AutoSeller initialized. F7=MAX+beat-seller; Auto Place OFF.");
        return true;
    }
    private void Save() => File.WriteAllText(configPath, JsonConvert.SerializeObject(options, Formatting.Indented));
    private void Dump()
    {
        inspector = adapter.CaptureTree();
        diagnostics.Dump(new { capturedAt = DateTimeOffset.UtcNow, tree = inspector, selected = adapter.CurrentPair(),
            fullTree = adapter.CaptureTree(true), snapshot = adapter.Snapshot(), catalog = adapter.Catalog,
            favorites = adapter.ReadFavorites(),
            stock = adapter.ReadFavorites().Pairs.Select(p => new { currency = p.Have, stock = adapter.ScanInventory(p.Have) }).ToArray(),
            picker = new { local = adapter.PickerElement?.IsVisibleLocal, visible = adapter.PickerElement?.IsVisible },
            rawWanted = adapter.Panel.WantedItemStock.Select(e => new { e.Get, e.Give, e.ListedCount }).ToArray(),
            rawOffered = adapter.Panel.OfferedItemStock.Select(e => new { e.Get, e.Give, e.ListedCount }).ToArray() });
        status = "Dumped " + diagnostics.LastFile;
    }
    private void Start(bool prepare, bool current = false)
    {
        if (executor.Running || filling) return;
        var policy = options.Policy;
        var explicitPairs = current && adapter.CurrentPair() != null ? new[] { adapter.CurrentPair() } : null;
        _ = Task.Run(async () => { try { await executor.Run(policy, options.PairRules, prepare, explicitPairs); }
            catch (Exception ex) { status = ex.Message; diagnostics.Log(ex.ToString()); } });
    }
    public override void Render()
    {
        if (!Settings.Enable.Value || !Settings.ShowOverlay.Value || adapter == null) return;
        try
        {
            if (GameController.Window.IsForeground())
            {
                if (Settings.StopKey.PressedOnce()) Stop();
                if (Settings.ScanKey.PressedOnce()) Scan();
                if (Settings.PreviewKey.PressedOnce()) FillSelectedMaximum();
                if (Settings.StartKey.PressedOnce()) Start(true);
                if (Settings.AutoKey.PressedOnce() && !executor.Running && !filling)
                { options.AutoPlaceOrders = !options.AutoPlaceOrders; Save(); status = options.AutoPlaceOrders ? "Auto Place ON: next Start may submit orders" : "Auto Place OFF"; }
            }
            if (DateTime.UtcNow > nextRequestCheck)
            {
                nextRequestCheck = DateTime.UtcNow.AddSeconds(1);
                if (!executor.Running && File.Exists(configPath) && File.GetLastWriteTimeUtc(configPath) > configWritten)
                {
                    var fresh = JsonConvert.DeserializeObject<AutoSellerSettings>(File.ReadAllText(configPath));
                    options.ExchangePanelPath = fresh.ExchangePanelPath;
                    options.FavoritesRootPath = fresh.FavoritesRootPath;
                    options.FavoritesWantOnLeftVerified = fresh.FavoritesWantOnLeftVerified;
                    options.MarketMapping = fresh.MarketMapping;
                    options.GoldCostPath = fresh.GoldCostPath;
                    options.PlaceButtonPath = fresh.PlaceButtonPath;
                    options.PlacementLayoutVerified = fresh.PlacementLayoutVerified;
                    configWritten = File.GetLastWriteTimeUtc(configPath);
                }
                string request = Path.Combine(diagnostics.DirectoryPath, "capture.request");
                if (File.Exists(request)) { File.Delete(request); Dump(); }
            }
            var s = adapter.Snapshot(); var size = ImGui.GetIO().DisplaySize;
            ImGui.SetNextWindowPos(new Vector2(Math.Max(15, size.X * .02f), Math.Max(15, size.Y * .37f)), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(Math.Min(740, size.X * .7f), Math.Min(650, size.Y * .6f)), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Faustus AutoSeller"))
            {
                ImGui.Text($"{executor.State} | Favorites: {favorites.Pairs.Count} | Slots: {Math.Max(0, 10 - s.Orders.Count)}/10 | Gold: {s.Gold?.ToString() ?? "unknown"}");
                if (!s.PanelOpen) ImGui.TextWrapped("Plugin loaded. Open Faustus -> Currency Exchange.");
                ImGui.TextWrapped(executor.Status); ImGui.TextWrapped(status);
                bool busy = executor.Running || filling;
                if (busy) ImGui.BeginDisabled();
                if (ImGui.Button("Scan Favorites")) Scan();
                ImGui.SameLine();
                if (ImGui.Button("Refresh Inventory")) Scan();
                ImGui.SameLine(); if (ImGui.Button("Preview Sales")) Start(false);
                ImGui.SameLine(); if (ImGui.Button("Start Selling")) Start(true);
                if (busy) ImGui.EndDisabled();
                ImGui.SameLine(); if (ImGui.Button("EMERGENCY STOP")) Stop();
                ImGui.Text($"F6 scan | F7 fill MAX + beat seller | F8 start | F9 STOP | F10 Auto Place: {(options.AutoPlaceOrders ? "ON" : "OFF")}");
                if (busy) ImGui.BeginDisabled();
                if (ImGui.Button("F7: Fill current pair MAX + beat seller")) FillSelectedMaximum();
                if (busy) ImGui.EndDisabled();
                if (diagnostics.HasPending)
                {
                    ImGui.TextWrapped("Unconfirmed order journal. Check active orders and balances before clearing.");
                    if (!busy && ImGui.Button("I reconciled active orders — clear pending journal")) { diagnostics.ClearPending(); status = "Pending journal cleared by user"; }
                }
                if (ImGui.CollapsingHeader("Pricing and limits"))
                {
                    if (busy) ImGui.BeginDisabled();
                    int mode = (int)options.PricingMode;
                    if (ImGui.Combo("Pricing Mode", ref mode, "Beat seller (minimum integer step)\0Match seller\0Undercut %\0Instant exchange (bids)\0")) { options.PricingMode = (PricingMode)mode; Save(); }
                    float percent = (float)options.UndercutPercent;
                    if (ImGui.InputFloat("Undercut %", ref percent)) { options.UndercutPercent = (decimal)Math.Clamp(percent, 0, 50); Save(); }
                    float max = (float)options.MaximumActualDiscount;
                    if (ImGui.InputFloat("Max actual discount %", ref max)) { options.MaximumActualDiscount = (decimal)Math.Clamp(max, .01f, 50); Save(); }
                    int min = options.MinimumQuantity, reserve = options.KeepReserve, orders = options.MaximumOrders;
                    if (ImGui.InputInt("Minimum Quantity", ref min)) { options.MinimumQuantity = Math.Max(1, min); Save(); }
                    if (ImGui.InputInt("Keep Reserve", ref reserve)) { options.KeepReserve = Math.Max(0, reserve); Save(); }
                    if (ImGui.InputInt("Maximum orders per session", ref orders)) { options.MaximumOrders = Math.Clamp(orders, 1, 10); Save(); }
                    bool auto = options.AutoPlaceOrders;
                    if (ImGui.Checkbox("Auto Place Orders", ref auto)) { options.AutoPlaceOrders = auto; Save(); }
                    ImGui.TextWrapped("OFF: prepare, then you click Place Order. Automation may violate GGG rules. Auto Place requires verified layout, market direction, gold and order acknowledgement; never resumes on reload.");
                    if (busy) ImGui.EndDisabled();
                }
                ImGui.Separator();
                foreach (var pair in favorites.Pairs)
                {
                    var stock = adapter.ScanInventory(pair.Have);
                    ImGui.Text($"{pair.Want.Name} <- {pair.Have.Name}: {stock.Available?.ToString() ?? "unknown"} available ({stock.Inventory} inventory + {stock.KnownStash} loaded stash; {stock.ReservedInOrders} escrow)");
                }
                foreach (var row in executor.Rows)
                {
                    ImGui.Text($"{row.Pair.Want.Name} <- {row.Pair.Have.Name} | {row.Status}");
                    if (row.Quote.Valid) ImGui.Text($"  {row.Quote.Want}:{row.Quote.Have} | seller {row.Quote.ReferencePrice:0.######} -> {row.Quote.SalePrice:0.######} | discount {row.Quote.ActualDiscount:0.####}% | lot {row.Quote.MinimumLot}");
                }
                if (ImGui.CollapsingHeader("Selected pair / legacy one-click 1% fill"))
                {
                    ImGui.TextWrapped("Current pair fallback, not a Favorites scan. Old 1% filling retained with verified market direction; never submits by itself.");
                    ImGui.InputInt("Manual HAVE quantity", ref manualQuantity);
                    var pair = adapter.CurrentPair();
                    if (pair != null)
                    {
                        ImGui.Text(pair.Want.Name + " <- " + pair.Have.Name);
                        int quantity = GameUiAdapter.ReadInteger(adapter.HaveInput) ?? manualQuantity;
                        var quote = PricingEngine.Calculate(pair, quantity, adapter.ReadMarket(pair), options.Policy with { Mode = PricingMode.Undercut, UndercutPercent = 1 });
                        ImGui.TextWrapped(quote.Valid ? $"1% fill: {quote.Want} WANT / {quote.Have} HAVE" : quote.Error);
                        if (!busy && quote.Valid && ImGui.Button("Fill 1% quote — do not place"))
                        {
                            filling = true; manualAction = new(); var token = manualAction.Token;
                            _ = Task.Run(async () => { try { await adapter.Fill(pair, quote.Want, quote.Have, token); status = "Filled; review and place manually"; }
                                catch (Exception ex) { status = ex.Message; } finally { filling = false; } });
                        }
                        if (!busy && ImGui.Button("Preview current pair")) Start(false, true);
                    }
                }
                if (ImGui.CollapsingHeader("Debug / UI Inspector")) DrawInspector(busy);
            }
            ImGui.End();
        }
        catch (Exception ex) { status = "UI error: " + ex.Message; }
    }
    private void DrawInspector(bool busy)
    {
        if (ImGui.Button("Capture UI tree + market + catalog")) Dump();
        ImGui.InputText("Favorites root path", ref favoritesPath, 128);
        ImGui.InputText("Gold cost path", ref goldPath, 128);
        ImGui.InputText("Place button path", ref placePath, 128);
        bool left = options.FavoritesWantOnLeftVerified, layout = options.PlacementLayoutVerified;
        ImGui.Checkbox("Verified Favorites: left WANT / right HAVE", ref left);
        ImGui.Checkbox("Verified gold / Place Order bindings", ref layout);
        int mapping = (int)options.MarketMapping;
        ImGui.Combo("Market feed mapping", ref mapping, "Unverified — pricing blocked\0WantedStock = sellers\0OfferedStock = sellers\0");
        if (!busy && ImGui.Button("Save verified bindings"))
        {
            options.FavoritesRootPath = favoritesPath; options.GoldCostPath = goldPath; options.PlaceButtonPath = placePath;
            options.FavoritesWantOnLeftVerified = left; options.PlacementLayoutVerified = layout; options.MarketMapping = (MarketFeedMapping)mapping; Save();
        }
        ImGui.InputText("Tree filter", ref inspectorFilter, 128);
        if (inspector != null)
            foreach (var n in inspector.Walk().Where(n => string.IsNullOrEmpty(inspectorFilter) || (n.Path + n.Text + n.Metadata).Contains(inspectorFilter, StringComparison.OrdinalIgnoreCase)))
                ImGui.TextWrapped($"{n.Path} [{(n.Visible ? "visible" : "hidden")}/{(n.Active ? "active" : "inactive")}] {n.X:0},{n.Y:0} {n.Width:0}x{n.Height:0} {n.Text} {n.Metadata}");
    }
    private void Scan()
    {
        if (filling || executor.Running) return;
        filling = true; manualAction = new(); var token = manualAction.Token;
        _ = Task.Run(async () => { try { await adapter.OpenFavorites(token); favorites = adapter.ReadFavorites(); status = favorites.Diagnostic; }
            catch (Exception ex) { status = ex.Message; } finally { filling = false; } });
    }
    private void FillSelectedMaximum()
    {
        if (filling || executor.Running) return;
        filling = true; manualAction = new(); var token = manualAction.Token;
        var policy = options.Policy;
        status = "F7: refreshing available stock and seller price...";
        _ = Task.Run(async () =>
        {
            try
            {
                var quote = await SelectedPairFiller.FillMaximum(adapter, policy, token);
                status = $"F7 filled: WANT {quote.Want} / HAVE {quote.Have}; {quote.ActualDiscount:0.####}% below seller. Review and click Place Order manually.";
                diagnostics.Log(status);
            }
            catch (Exception ex) { status = "F7: " + ex.Message; diagnostics.Log(status); }
            finally { filling = false; }
        });
    }
    private void Stop() { executor?.Stop(); manualAction?.Cancel(); }
    public override void OnPluginDestroyForHotReload() { Stop(); }
}
