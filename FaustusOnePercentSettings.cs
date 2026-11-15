using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Windows.Forms;

namespace FaustusOnePercent;

public sealed class FaustusOnePercentSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);

    [Menu("Show overlay", "Show the one-click 1% undercut tool beside the Currency Exchange window.")]
    public ToggleNode ShowOverlay { get; set; } = new(true);
    [Menu("Scan Favorites hotkey")]
    public HotkeyNodeV2 ScanKey { get; set; } = new(Keys.F6);
    [Menu("Fill maximum, seller total minus 1 Chaos hotkey", "I WANT must be Chaos Orb. Fill all available HAVE at floor(total seller value) minus 1 Chaos; no percentage cap. Does not place an order.")]
    public HotkeyNodeV2 PreviewKey { get; set; } = new(Keys.F7);
    [Menu("Start selling hotkey")]
    public HotkeyNodeV2 StartKey { get; set; } = new(Keys.F8);
    [Menu("Emergency stop hotkey")]
    public HotkeyNodeV2 StopKey { get; set; } = new(Keys.F9);
    [Menu("Toggle Auto Place hotkey", "Only toggles the mode; does not start a sale. Automation may violate GGG rules.")]
    public HotkeyNodeV2 AutoKey { get; set; } = new(Keys.F10);

    [Menu("Max rounding undercut (%)", "Block fills where rounding produces a discount greater than this percentage.")]
    public RangeNode<int> MaxActualDiscountPercent { get; set; } = new(2, 1, 10);

    [Menu("Verify filled numbers", "Read both inputs back from game memory after writing them.")]
    public ToggleNode VerifyFills { get; set; } = new(true);

    [Menu("Cancel when mouse moves", "Avoid typing into an unrelated window if the cursor moves during filling.")]
    public ToggleNode AbortOnMouseMove { get; set; } = new(true);

    [Menu("Maximum retries", "How many times to retry filling a field if verification fails.")]
    public RangeNode<int> MaxRetries { get; set; } = new(3, 1, 6);
}
