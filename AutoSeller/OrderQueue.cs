using System;
using System.Collections.Generic;
using System.Linq;

namespace FaustusOnePercent.AutoSeller;

public static class OrderQueue
{
    public static List<SaleRow> Build(IEnumerable<FavoritePair> pairs, IEnumerable<PairRule> rules,
        Func<Currency, StockEvidence> scanner, SellerPolicy policy)
    {
        var reserved = new Dictionary<string, long>();
        var output = new List<SaleRow>();
        foreach (var pair in pairs.GroupBy(p => p.Key).Select(g => g.First()))
        {
            var rule = rules.FirstOrDefault(r => r.WantMetadata == pair.Want.Metadata && r.HaveMetadata == pair.Have.Metadata)
                ?? new PairRule { WantMetadata = pair.Want.Metadata, HaveMetadata = pair.Have.Metadata,
                    KeepReserve = policy.KeepReserve, MinimumQuantity = policy.MinimumQuantity };
            var stock = scanner(pair.Have);
            var row = new SaleRow { Pair = pair, Rule = rule, Stock = stock };
            output.Add(row);
            if (!pair.Verified) { row.Status = "Unverified favorite direction"; continue; }
            if (pair.Want.Metadata == pair.Have.Metadata) { row.Status = "Self trade rejected"; continue; }
            if (!stock.Available.HasValue) { row.Status = "Stock unknown (unloaded stash)"; continue; }
            if (rule.MinimumQuantity < 1 || rule.KeepReserve < 0 || rule.MaximumQuantity < 0 || rule.MinimumPrice < 0)
            { row.Status = "Invalid quantity/reserve limits"; continue; }
            long available = Math.Max(0, stock.Available.Value - reserved.GetValueOrDefault(pair.Have.Metadata) - rule.KeepReserve);
            if (rule.MaximumQuantity > 0) available = Math.Min(available, rule.MaximumQuantity);
            available = Math.Min(available, int.MaxValue);
            if (available < rule.MinimumQuantity) { row.Status = "Below minimum / reserved by earlier favorite"; continue; }
            row.Quote = new(false, "Not priced", 0, (int)available, 0, 0, 0, 0);
            reserved[pair.Have.Metadata] = reserved.GetValueOrDefault(pair.Have.Metadata) + available;
            row.Status = "Queued";
        }
        return output;
    }
}
