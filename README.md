# FaustusOnePercent — PoE 1 / ExileCore

A one-button, human-confirmed 1% undercut tool for the **Currency Exchange** at Faustus.

## What it does

1. In game, choose `I WANT` and `I HAVE` currencies in Faustus's Currency Exchange.
2. Enter how many units of `I HAVE` to sell **in Faustus's own I HAVE input field**. The plugin reads that amount automatically. If this is unavailable, uncheck **Read I HAVE amount from Faustus** and enter an amount manually in the overlay.
3. The overlay reads the visible market listings for this pair, takes the **lowest I WANT / I HAVE** ratio, and calculates 1% less `I WANT` at your fixed `I HAVE` quantity.
4. Press **Fill 1% undercut**. In the default mode, the plugin fills **only I WANT** and verifies I HAVE has not changed. In manual mode, it fills both numbers. It optionally reads back the values to verify them.
5. **You must click `Place Order` yourself.** This plugin does not trade automatically, send orders, monitor inventories, or repeat trades.

Example: sell 5 Divine Orbs when the best ratio is 100 Chaos/Divine -> fill `I WANT 495` Chaos, `I HAVE 5` Divine.

## Safety and limitations

- This is a third-party automation plugin; use at your own risk and check Grinding Gear Games' rules.
- Rounding up would fail to undercut, so rounding is downward. If the resulting real discount exceeds the configurable limit (2% by default), the plugin **blocks filling**. Try a larger amount.
- The game can return unfilled currency for large order lots. The overlay warns when the reduced ratio requires more than 200 units on the I HAVE side per minimum whole lot.
- It reads the visible `OfferedItemStock` feed used by `CurrencyRatioExchange`. A sparse, outdated or unusual order book can yield an undesirable benchmark. Always verify the displayed quote before placing an order.
- Currency quantity is taken from the I HAVE field by default, or entered manually in fallback mode; it is not a claim that you have that many units available. The game validates your actual currency.
- While this diagnostic build is enabled, the overlay stays visible even when Currency Exchange is closed. If ExileCore does not detect the exchange, the overlay reports whether the panel is missing or marked invisible.
- The overlay stays anchored over the lower part of the exchange while visible. The currency-picker visibility flag is ignored to avoid hiding the overlay in some ExileCore builds.
- If no overlay is visible anywhere (including while exchange is closed), first check that ExileCore has successfully compiled and enabled this plugin in HUD Settings. Look for build errors and plugin load messages.
- The names / UI tree may change with game or ExileCore updates.
- **Build and in-game behavior have not been verified in this environment.** Only the source is provided.

## Install / Build

1. Get a current PoE 1 ExileCore / ExileAPI build compatible with the current game patch.
2. Put this folder under `ExileCore/Plugins/Source/FaustusOnePercent/`.
3. Install the **.NET 10 SDK** (needed for manual builds).
4. From the plugin folder run:

   ```powershell
   dotnet build -c Release
   ```

   If ExileCore DLLs aren't in the ancestor installation root, pass its folder explicitly:

   ```powershell
   dotnet build -c Release -p:ExileCorePath="C:\path\to\ExileCore"
   ```

5. Restart ExileCore and enable `FaustusOnePercent` under Plugins. Enable **Show overlay**.
6. Open Faustus -> Currency Exchange, choose both currencies, close the currency picker, enter the amount to sell in the in-game I HAVE field, verify the preview, and press **Fill 1% undercut**.

## Optional math tests

```powershell
dotnet run --project Tests/QuoteMathTests.csproj
```

## Compatibility notes

Targets `net10.0-windows`, `x64`, and the newer `Hexa.NET.ImGui` API. References `ExileCore.dll`, `GameOffsets.dll`, and `Hexa.NET.ImGui.dll` from ExileCore. The exact DLL layout may differ by installed ExileCore distribution. The code needs to be tested against your specific build.

The model access and input strategy are based on the public, MIT-licensed [CurrencyRatioExchange](https://github.com/exCore-PoE1/CurrencyRatioExchange) plugin, but this is a separate implementation.
