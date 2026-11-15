using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FaustusOnePercent.AutoSeller;

public sealed record InventoryLocation(string Identity, bool IsStash, long Quantity);
public static class CurrencyInventoryScanner
{
    // Structured picker text, resolved to the exchange metadata catalog. No OCR or icon matching.
    public static IReadOnlyDictionary<string, long> ReadPickerOwned(UiNode tree, IEnumerable<Currency> currencies)
    {
        var output = new Dictionary<string, long>();
        if (tree == null) return output;
        var catalog = currencies.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in tree.Walk().Where(n => n.Path.StartsWith("exchange/20/6/") && n.Visible && n.LocalVisible
            && n.Width > 200 && n.Width < 350 && n.Height >= 30 && n.Height < 85))
        {
            var cells = row.Walk().ToArray();
            var identities = cells.Select(n => catalog.GetValueOrDefault((n.Text ?? "").Trim())).Where(c => c != null).Distinct().ToArray();
            var countText = cells.Where(n => Regex.IsMatch(n.Text ?? "", @"^\d[\d\s,]*$")).Select(n => n.Text).ToArray();
            if (identities.Length == 1 && countText.Length == 1 && long.TryParse(new string(countText[0].Where(char.IsDigit).ToArray()), out long count))
            {
                // Popular and All can show the same currency. A disagreement is unsafe, not an invitation to add them.
                var key = identities[0].Metadata;
                if (output.TryGetValue(key, out long prior) && prior != count) output[key] = -1;
                else if (!output.ContainsKey(key)) output[key] = count;
            }
        }
        return output.Where(p => p.Value >= 0).ToDictionary(p => p.Key, p => p.Value);
    }
    public static StockEvidence Combine(IEnumerable<InventoryLocation> locations, long? pickerOwned,
        long reserved, bool complete, bool hasSnapshot, bool includesReserved = false)
    {
        var unique = locations.GroupBy(x => x.Identity).Select(g => g.First()).ToArray();
        var inventory = unique.Where(x => !x.IsStash).Sum(x => Math.Max(0, x.Quantity));
        var stash = unique.Where(x => x.IsStash).Sum(x => Math.Max(0, x.Quantity));
        return new(inventory, stash, pickerOwned, Math.Max(0, reserved), includesReserved, complete,
            hasSnapshot, pickerOwned.HasValue ? "Picker-owned verified; order escrow not subtracted twice"
                : complete ? "Known stock" : "Partial stash: known lower bound only; missing stock is unknown");
    }
}
