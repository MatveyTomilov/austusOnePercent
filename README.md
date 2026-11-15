# Faustus AutoSeller — PoE 1 / ExileCore

## F7: fill the selected pair

1. Open Faustus's Currency Exchange and choose **I WANT** and **I HAVE**.
2. Close the currency picker and press **F7**.
3. The plugin refreshes the in-game owned count for the selected HAVE currency, fills **all available units**, and fills WANT at the smallest integer step below the cheapest competing seller.
4. Review both numbers and click **Place Order** yourself.

F7 uses in-game sell offers, not poe.ninja or instant-exchange bids. For a seller price `p` and available quantity `q`, WANT is `ceil(q * p) - 1`. For example, 99 Scouring at a competing price of 17 Chaos / 20 Scouring becomes **84 Chaos / 99 Scouring**. No fixed 1% discount is applied. The actual discount, including rounding, must remain within the configured limit (2% by default). If the lot is too small to improve the price safely, the fill is refused.

F7 operates only on the selected pair. It is independent of Favorites, queue pricing mode, queue reserve limits and the pending-order journal. It never submits an order, including when Auto Place is enabled for the queue. It verifies both currencies, game focus and both filled inputs; F9 cancels an operation. Do not move the mouse while the fields are being filled.

## Other functionality retained

- **F6:** read Favorites and refresh stock.
- **Preview Sales:** preview the Favorites queue (still available as a button).
- **F8 / Start Selling:** prepare the Favorites queue for manual confirmation, or use its separately enabled Auto Place mode.
- **F9:** emergency stop.
- **F10:** explicitly toggle Auto Place for the queue. Auto Place starts OFF after every plugin reload.
- Pricing modes: minimum integer step, match seller, percentage undercut, instant exchange.
- Legacy one-click 1% filling remains in the selected-pair section.
- UI Inspector exports element paths, text, metadata, local/inherited visibility and bounds.

The queue's automatic submission and acknowledgement have unit coverage, but the full two-order automatic sequence has **not** been validated live. A manual queue run did detect two Favorites, confirm available stock and fill the first order. A focus change stopped it before submission. An uncertain queue result keeps a pending journal until the user reconciles active orders. This journal does not prevent F7 fills.

## Installation and build

Use one source folder under `ExileCore/Plugins/Source/`. Keep the class/plugin name `FaustusOnePercent`; the overlay is named **Faustus AutoSeller**. Do not install a second copy alongside it.

The project targets `net10.0-windows`, x64, and the host's Hexa.NET.ImGui. A .NET 10 SDK is needed for manual builds; the installed host's DLLs are referenced rather than redistributed.

```powershell
dotnet build -c Release -p:ExileCorePath="C:\path\to\ExileCore"
dotnet run --project Tests/QuoteMathTests.csproj
```

Load/reload the plugin through ExileCore. Hotkeys can be changed in HUD Settings. The existing serialized `PreviewKey` setting now invokes the F7 selected-pair fill, retaining its configured key.

## Configuration and diagnostics

Files are under the host's `config/FaustusAutoSeller/` directory:

- `settings.json`: pricing, queue limits and validated UI bindings.
- `events.log`: operation status and errors.
- `exchange-ui.json`: requested UI/market/stock diagnostic export.
- `pending-order.json`: protection against duplicate queue submissions.

The audited English PoE 1 layout has WANT/HAVE inputs at exchange children 5/8, the currency picker at child 20, Favorites at `exchange/20/6/1/0/0`, gold cost at `exchange/15`, and Place Order text at `exchange/16/0`. In the audited host, the built-in `IngameUi.CurrencyExchangePanel` pointed at another hidden panel; the adapter resolves the actual exchange by semantic labels or a validated root binding. Do not assume root index 189 remains correct after an update.

In the verified market mapping, **OfferedItemStock** represents sellers from the opposite perspective, so raw `(Get, Give)` is normalized to `(Give, Get)` for our WANT:HAVE. **WantedItemStock** is already normalized for instant exchange bids. Unverified mapping blocks pricing. A fresh installation requires checking/saving the bindings in UI Inspector; changing game language or host/game layout can invalidate them.

The inventory scanner deduplicates loaded locations and labels incomplete stash data as partial. F7 requires a newly confirmed picker-owned count or a complete inventory snapshot before calling its quantity a maximum. Existing order escrow is not subtracted twice from an available picker balance. Missing or inconsistent data causes a diagnostic error instead of guessing a quantity.

Third-party input automation may violate GGG rules. The implementation reads ExileCore data and sends ordinary UI input; it does not inject code, write game memory or install drivers/services.

The public [CurrencyRatioExchange](https://github.com/exCore-PoE1/CurrencyRatioExchange) implementation is an API/input reference. This plugin remains a separate implementation in the existing FaustusOnePercent repository.
