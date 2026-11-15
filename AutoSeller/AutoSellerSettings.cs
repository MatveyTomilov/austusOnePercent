using System.Collections.Generic;

namespace FaustusOnePercent.AutoSeller;

public sealed class AutoSellerSettings
{
    public PricingMode PricingMode { get; set; } = PricingMode.BeatCompetitor;
    public decimal UndercutPercent { get; set; } = 1;
    public decimal MaximumActualDiscount { get; set; } = 2;
    public int MinimumQuantity { get; set; } = 1;
    public int KeepReserve { get; set; }
    public int MaximumOrders { get; set; } = 5;
    public bool AutoPlaceOrders { get; set; } // Deliberately false by default.
    public string FavoritesRootPath { get; set; } = "";
    public string ExchangePanelPath { get; set; } = "";
    public bool FavoritesWantOnLeftVerified { get; set; }
    public MarketFeedMapping MarketMapping { get; set; }
    public string GoldCostPath { get; set; } = "";
    public string PlaceButtonPath { get; set; } = "";
    public bool PlacementLayoutVerified { get; set; }
    public List<PairRule> PairRules { get; set; } = new();
    public SellerPolicy Policy => new(PricingMode, UndercutPercent, MaximumActualDiscount,
        MinimumQuantity, KeepReserve, MaximumOrders, AutoPlaceOrders);
}
