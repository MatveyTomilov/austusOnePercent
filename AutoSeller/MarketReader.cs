using System;
using System.Collections.Generic;
using System.Linq;

namespace FaustusOnePercent.AutoSeller;

public enum MarketFeedMapping { Unverified, WantedStockIsCompetitors, OfferedStockIsCompetitors }
public static class MarketReader
{
    public static MarketBook Normalize(FavoritePair pair, IEnumerable<MarketOrder> wantedStock,
        IEnumerable<MarketOrder> offeredStock, MarketFeedMapping mapping, DateTimeOffset now)
    {
        // Raw pairs are (Get, Give). In this host OfferedItemStock is the opposing
        // perspective: competitor GETS our HAVE and GIVES our WANT. Invert it.
        // WantedItemStock is already our WANT:HAVE (instant exchange bids).
        var wanted = wantedStock.ToArray();
        var offered = offeredStock.Select(x => new MarketOrder(x.Have, x.Want, x.ListedCount)).ToArray();
        var asks = mapping == MarketFeedMapping.OfferedStockIsCompetitors ? offered : wanted;
        var bids = mapping == MarketFeedMapping.OfferedStockIsCompetitors ? wanted : offered;
        return new(pair, asks, bids,
            now, mapping != MarketFeedMapping.Unverified,
            mapping == MarketFeedMapping.Unverified ? "Select and validate feed mapping using diagnostics" : mapping.ToString());
    }
}
