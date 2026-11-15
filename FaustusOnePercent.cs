using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using FaustusOnePercent.Utils;
using Hexa.NET.ImGui;

namespace FaustusOnePercent;

/// <summary>
/// Human-in-the-loop Currency Exchange quote assistant for PoE 1.
/// Never clicks Place Order and never automates a sequence of market trades.
/// </summary>
public sealed class FaustusOnePercent : BaseSettingsPlugin<FaustusOnePercentSettings>
{
    private string _amountText = "100";
    private bool _readHaveFromGame = true;
    private string _status = string.Empty;
    private volatile bool _filling;
    private const int ClickDelayMs = 95;
    private const int VerifyIntervalMs = 35;
    private const int VerifyTimeoutMs = 700;
    private const int MaxMouseDriftSquared = 144;

    public override bool Initialise()
    {
        LogMessage("FaustusOnePercent initialized.");
        return true;
    }

    public override void Render()
    {
        if (!Settings.Enable.Value || !Settings.ShowOverlay.Value)
            return;

        var panel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
        bool exchangeVisible = panel != null && panel.IsVisible;

        // Diagnostic mode: always show the window while the plugin is enabled.
        // This makes a missing/stale game UI element distinguishable from a plugin
        // that failed to load. The picker visibility flag is intentionally ignored:
        // it can remain true even after the selector is closed in some game builds.
        Vector2 overlayPos = exchangeVisible
            ? new Vector2(panel.GetClientRectCache.X + 25, panel.GetClientRectCache.Y + 280)
            : new Vector2(730, 230);
        ImGui.SetNextWindowPos(overlayPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(430, 0), ImGuiCond.Always);

        if (ImGui.Begin("Faustus 1% Undercut", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
        {
            if (!exchangeVisible)
            {
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Plugin loaded; waiting for Currency Exchange.");
                ImGui.TextWrapped(panel == null
                    ? "ExileCore CurrencyExchangePanel is null. Open Faustus -> Currency Exchange."
                    : "ExileCore reports CurrencyExchangePanel.IsVisible = false. Try closing and reopening the exchange.");
            }
            else
            {
                ImGui.Text("Sell the selected I HAVE currency for I WANT.");
            ImGui.Checkbox("Read I HAVE amount from Faustus", ref _readHaveFromGame);
            int have = 0;
            bool validAmount;
            if (_readHaveFromGame)
            {
                have = ReadFieldInt(panel.OfferedItemCountInput) ?? 0;
                validAmount = have > 0;
                ImGui.Text($"I HAVE in exchange: {have}");
            }
            else
            {
                ImGui.Text("I HAVE quantity to sell:");
                ImGui.SetNextItemWidth(160);
                ImGui.InputText("##HaveAmount", ref _amountText, 16);
                validAmount = int.TryParse(_amountText, out have) && have > 0;
            }
            var orders = ReadMarketOrdersFromGame();
            var quote = validAmount
                ? QuoteMath.Calculate(have, orders, Settings.MaxActualDiscountPercent.Value)
                : QuoteResult.Invalid("Set a positive I HAVE amount in the exchange (or use manual mode).");

            ImGui.Separator();
            ImGui.Text($"Market offers read: {orders.Count}");
            if (quote.IsValid)
            {
                ImGui.Text($"Best rival ratio (Want:Have): {quote.ReferenceOrder.Want}:{quote.ReferenceOrder.Have}");
                ImGui.Text($"Undercut: {quote.ActualUndercutPercent:0.##}% including rounding");
                ImGui.TextColored(new Vector4(0.35f, 1f, 0.45f, 1f),
                    $"Fill I WANT = {quote.Want} / I HAVE = {quote.Have}");

                if (quote.MinimalHaveLot > 200)
                    ImGui.TextWrapped($"Caution: minimum whole lot is {quote.MinimalHaveLot} I HAVE. " +
                        "Partial fills may return unused currency; verify the final order.");

                bool disabled = _filling;
                if (disabled) ImGui.BeginDisabled();
                bool clicked = ImGui.Button("Fill 1% undercut (do NOT place order)", new Vector2(-1, 40));
                if (!disabled && clicked)
                {
                    _filling = true;
                    _status = "Filling...";
                    int want = quote.Want;
                    int amount = quote.Have;
                    string selectedType = panel.OfferedItemType?.Metadata;
                    bool preserveHave = _readHaveFromGame;
                    _ = Task.Run(async () =>
                    {
                        try { await FillExchangeAsync(want, amount, selectedType, preserveHave); }
                        catch (Exception ex) { _status = $"Fill error: {ex.Message}"; LogError(ex.ToString()); }
                        finally { _filling = false; }
                    });
                }
                if (disabled) ImGui.EndDisabled();
            }
            else
            {
                ImGui.TextWrapped($"Cannot fill: {quote.Error}");
            }

            if (!string.IsNullOrEmpty(_status))
                ImGui.TextWrapped(_status);
            ImGui.Separator();
            ImGui.TextWrapped("Review both values and click Place Order YOURSELF.");
            }
        }
        ImGui.End();
    }

    private List<MarketOrder> ReadMarketOrdersFromGame()
    {
        var output = new List<MarketOrder>();
        try
        {
            var stock = GameController.IngameState?.IngameUi?.CurrencyExchangePanel?.OfferedItemStock;
            if (stock == null)
                return output;

            foreach (var entry in stock)
            {
                if (entry == null || entry.Get <= 0 || entry.Give <= 0 || entry.ListedCount <= 0)
                    continue;
                // Other player's Get = our Have; their Give = our Want.
                output.Add(new MarketOrder(entry.Give, entry.Get, (int)entry.ListedCount));
            }
        }
        catch
        {
            // The panel may close while its market data is being read.
            output.Clear();
        }
        return output;
    }

    private async Task FillExchangeAsync(int want, int have, string selectedHaveMetadata, bool preserveHave)
    {
        var panel = GameController.IngameState?.IngameUi?.CurrencyExchangePanel;
        if (panel == null || !panel.IsVisible)
        {
            _status = "Currency Exchange was closed.";
            return;
        }
        if (panel.OfferedItemType?.Metadata != selectedHaveMetadata)
        {
            _status = "I HAVE currency changed. Fill cancelled.";
            return;
        }

        var wantInput = panel.WantedItemCountInput;
        var haveInput = panel.OfferedItemCountInput;
        if (wantInput == null || haveInput == null)
        {
            _status = "Cannot find I WANT / I HAVE fields.";
            return;
        }

        var window = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
        var offset = new Vector2(window.X, window.Y);
        var cursor = Mouse.GetCursorPosition();
        bool verify = Settings.VerifyFills.Value;
        bool abortOnMove = Settings.AbortOnMouseMove.Value;
        int retries = Settings.MaxRetries.Value;

        try
        {
            if (preserveHave && ReadFieldInt(haveInput) != have)
            {
                _status = "I HAVE amount changed. Fill cancelled.";
                return;
            }

            bool wantOk = await FillFieldAsync(wantInput, want, offset, verify, abortOnMove, retries);
            bool haveOk = wantOk && (preserveHave
                ? ReadFieldInt(haveInput) == have
                : await FillFieldAsync(haveInput, have, offset, verify, abortOnMove, retries));
            _status = wantOk && haveOk
                ? $"Filled {want} : {have}. Check and PLACE ORDER manually."
                : "Verification failed. Inspect both fields; do NOT place the order blindly.";
        }
        catch (OperationCanceledException)
        {
            _status = "Cancelled: mouse moved during input.";
        }
        finally
        {
            // Never position the cursor onto Place Order.
            if (!abortOnMove || _status != "Cancelled: mouse moved during input.")
                Mouse.SetPosition(new Vector2(cursor.X, cursor.Y));
        }
    }

    private static async Task<bool> FillFieldAsync(
        Element field, int value, Vector2 offset, bool verify, bool abortOnMove, int retries)
    {
        for (int attempt = 0; attempt < (verify ? Math.Max(1, retries) : 1); attempt++)
        {
            var center = field.GetClientRectCache.Center;
            var point = new Vector2(center.X, center.Y) + offset;
            await Mouse.MoveMouse(point);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            await Task.Delay(ClickDelayMs);
            CheckMouse(point, abortOnMove);
            await Mouse.LeftDown();
            await Mouse.LeftUp();
            await Task.Delay(ClickDelayMs);

            // Clear the old number and type the new integer.
            for (int i = 0; i < 12; i++)
            {
                CheckMouse(point, abortOnMove);
                await Keyboard.KeyPress(Keys.Back);
            }
            foreach (char c in value.ToString())
            {
                CheckMouse(point, abortOnMove);
                await Keyboard.Type(c.ToString());
            }

            if (!verify)
                return true;

            int elapsed = 0;
            while (elapsed < VerifyTimeoutMs)
            {
                CheckMouse(point, abortOnMove);
                if (ReadFieldInt(field) == value)
                    return true;
                await Task.Delay(VerifyIntervalMs);
                elapsed += VerifyIntervalMs;
            }
        }
        return false;
    }

    private static int? ReadFieldInt(Element field)
    {
        if (field == null) return null;
        try
        {
            string text = field.TextNoTags;
            if (string.IsNullOrWhiteSpace(text))
                text = field.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                var node = field.FindChildRecursive(e =>
                    !string.IsNullOrWhiteSpace(e?.Text) && e.Text.Any(char.IsDigit));
                text = node?.Text;
            }
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var digits = new string(text.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var result) ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static void CheckMouse(Vector2 expected, bool enabled)
    {
        if (!enabled) return;
        var current = Mouse.GetCursorPosition();
        int dx = current.X - (int)expected.X;
        int dy = current.Y - (int)expected.Y;
        if ((long)dx * dx + (long)dy * dy > MaxMouseDriftSquared)
            throw new OperationCanceledException();
    }
}
