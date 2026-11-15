using System;
using FaustusOnePercent;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAILED: {name}");
    Console.WriteLine($"PASS: {name}");
}
var single = QuoteMath.Calculate(5, [new MarketOrder(100, 1, 100)]);
Check(single.IsValid && single.Want == 495 && single.Have == 5 && single.ActualUndercutPercent == 1m, "5 Divine -> 495 Chaos");
var selectedBest = QuoteMath.Calculate(10, [new MarketOrder(104, 1, 100), new MarketOrder(100, 1, 20)]);
Check(selectedBest.IsValid && selectedBest.Want == 990, "uses lowest competitor price");
Check(!QuoteMath.Calculate(1, [new MarketOrder(1, 3, 20)]).IsValid, "rejects zero result");
Check(!QuoteMath.Calculate(10, [new MarketOrder(1, 1, 20)]).IsValid, "rejects excessive integer rounding");
var ratio = QuoteMath.Calculate(100, [new MarketOrder(1, 1, 1)]);
Check(ratio.IsValid && ratio.Want == 99 && ratio.MinimalHaveLot == 100, "exact 1% with small integers");
Check(!QuoteMath.Calculate(100, [new MarketOrder(0, 1, 2)]).IsValid, "ignores invalid market orders");
Check(!QuoteMath.Calculate(0, [new MarketOrder(100, 1, 2)]).IsValid, "rejects invalid quantity");
await AutoSellerTests.Run();
Console.WriteLine("All tests passed.");
