using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace FaustusOnePercent;

public sealed class FaustusOnePercentSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);

    [Menu("Show overlay", "Show the one-click 1% undercut tool beside the Currency Exchange window.")]
    public ToggleNode ShowOverlay { get; set; } = new(true);

    [Menu("Max rounding undercut (%)", "Block fills where rounding produces a discount greater than this percentage.")]
    public RangeNode<int> MaxActualDiscountPercent { get; set; } = new(2, 1, 10);

    [Menu("Verify filled numbers", "Read both inputs back from game memory after writing them.")]
    public ToggleNode VerifyFills { get; set; } = new(true);

    [Menu("Cancel when mouse moves", "Avoid typing into an unrelated window if the cursor moves during filling.")]
    public ToggleNode AbortOnMouseMove { get; set; } = new(true);

    [Menu("Maximum retries", "How many times to retry filling a field if verification fails.")]
    public RangeNode<int> MaxRetries { get; set; } = new(3, 1, 6);
}
