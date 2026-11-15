# Faustus AutoSeller — PoE 1 / ExileCore

## Mouse5: sell the selected pair

1. Open Faustus's Currency Exchange, choose **Chaos Orb** in **I WANT**, and choose the currency to sell in **I HAVE**.
2. Close the currency picker and press **Mouse5 / XButton2**, the forward side button (usually the one farther from your thumb).
3. The plugin refreshes the in-game owned count for the selected HAVE currency, fills **all available units**, and fills WANT at the whole-stack competing seller quote rounded down, **minus 1 Chaos**.
4. The plugin verifies the selected pair, both numbers, available stock, gold, free order slots and fresh price, clicks **Place Order once**, and confirms a new matching order ID. One press processes only the current pair.

Mouse5 uses in-game sell offers, not poe.ninja or instant-exchange bids. For a seller price `p` and available quantity `q`, WANT is `floor(q * p) - 1`. For example, 5 essences at 5.9 Chaos each become **28 Chaos / 5 essences**. This applies to every HAVE currency, including essences. There is no percentage discount cap for this action; the configured percentage cap still applies to the queue. Zero or negative return is refused. WANT must be Chaos Orb so one Divine cannot accidentally be treated as one Chaos.

Mouse5 operates only on the selected pair. It is independent of Favorites, queue pricing mode, queue reserve limits and the queue's Auto Place toggle. The user explicitly enabled single-pair submission on this hotkey. The underlying fill-only component and legacy fill button still do not submit. **F9** cancels an operation; do not move the mouse during filling/submission. F7 no longer triggers the selected-pair action.

Before clicking Place Order, the action writes `selected-order-pending.json`. It clears that journal only after a matching new order is observed. A timeout, interruption or crash after a possible click blocks another Mouse5 submission until active orders have been reconciled and the dedicated unlock button is used. It does not retry an ambiguous click. Gold/layout/price failures before the click leave no pending submission.

## Other functionality retained

- **F6:** read Favorites and refresh stock.
- **Preview Sales:** preview the Favorites queue (still available as a button).
- **F8 / Start Selling:** prepare the Favorites queue for manual confirmation, or use its separately enabled Auto Place mode.
- **F9:** emergency stop.
- **F10:** explicitly toggle Auto Place for the queue. Auto Place starts OFF after every plugin reload.
- Pricing modes: minimum integer step, match seller, percentage undercut, instant exchange.
- Legacy one-click 1% filling remains in the selected-pair section.
- UI Inspector exports element paths, text, metadata, local/inherited visibility and bounds.

The queue's automatic submission and acknowledgement have unit coverage, but the full two-order automatic sequence has **not** been validated live. A manual queue run did detect two Favorites, confirm available stock and fill the first order. Selected-pair maximum filling has also succeeded live on multiple currencies. The new Mouse5 submission path has automated coverage; a physical-button placement still requires live verification. An uncertain queue result keeps its own pending journal until active orders are reconciled; the selected-pair action has a separate journal.

## Installation and build

Use one source folder under `ExileCore/Plugins/Source/`. Keep the class/plugin name `FaustusOnePercent`; the overlay is named **Faustus AutoSeller**. Do not install a second copy alongside it.

The project targets `net10.0-windows`, x64, and the host's Hexa.NET.ImGui. A .NET 10 SDK is needed for manual builds; the installed host's DLLs are referenced rather than redistributed.

```powershell
dotnet build -c Release -p:ExileCorePath="C:\path\to\ExileCore"
dotnet run --project Tests/QuoteMathTests.csproj
```

Load/reload the plugin through ExileCore. Hotkeys can be changed in HUD Settings. The existing serialized `PreviewKey` setting now invokes selected-pair selling; the default key is `XButton2`. On an existing installation, change its saved `ValueV2.Key` from `F7` to `XButton2` while the host is stopped, or rebind it through HUD Settings.

## Configuration and diagnostics

Files are under the host's `config/FaustusAutoSeller/` directory:

- `settings.json`: pricing, queue limits and validated UI bindings.
- `events.log`: operation status and errors.
- `exchange-ui.json`: requested UI/market/stock diagnostic export.
- `pending-order.json`: protection against duplicate queue submissions.
- `selected-order-pending.json`: independent protection against duplicate Mouse5 submissions.

The audited English PoE 1 layout has WANT/HAVE inputs at exchange children 5/8, the currency picker at child 20, Favorites at `exchange/20/6/1/0/0`, gold cost at `exchange/15`, and Place Order text at `exchange/16/0`. In the audited host, the built-in `IngameUi.CurrencyExchangePanel` pointed at another hidden panel; the adapter resolves the actual exchange by semantic labels or a validated root binding. Do not assume root index 189 remains correct after an update.

In the verified market mapping, **OfferedItemStock** represents sellers from the opposite perspective, so raw `(Get, Give)` is normalized to `(Give, Get)` for our WANT:HAVE. **WantedItemStock** is already normalized for instant exchange bids. Unverified mapping blocks pricing. A fresh installation requires checking/saving the bindings in UI Inspector; changing game language or host/game layout can invalidate them.

The inventory scanner deduplicates loaded locations and labels incomplete stash data as partial. The selected-pair action requires a newly confirmed picker-owned count or a complete inventory snapshot before calling its quantity a maximum. Existing order escrow is not subtracted twice from an available picker balance. Missing or inconsistent data causes a diagnostic error instead of guessing a quantity.

Third-party input automation may violate GGG rules. The implementation reads ExileCore data and sends ordinary UI input; it does not inject code, write game memory or install drivers/services.

The public [CurrencyRatioExchange](https://github.com/exCore-PoE1/CurrencyRatioExchange) implementation is an API/input reference. This plugin remains a separate implementation in the existing FaustusOnePercent repository.
