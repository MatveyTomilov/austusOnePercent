using System;
using System.Collections.Generic;
using System.Linq;

namespace FaustusOnePercent.AutoSeller;

public static class FavoritesReader
{
    // Reads identities, never artwork. UI column direction must be validated for this host layout.
    public static FavoritesResult Read(UiNode tree, IEnumerable<Currency> catalog, string rootPath,
        bool wantOnLeftConfirmed)
    {
        if (tree == null) return new(Array.Empty<FavoritePair>(), false, "Exchange UI unavailable");
        var byMeta = catalog.GroupBy(c => c.Metadata).ToDictionary(g => g.Key, g => g.First());
        var byName = catalog.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var all = tree.Walk().ToDictionary(n => n.Path);
        UiNode root = !string.IsNullOrWhiteSpace(rootPath) ? all.GetValueOrDefault(rootPath) : null;
        if (root == null)
        {
            var anchor = all.Values.FirstOrDefault(n => n.Visible
                && (string.Equals(n.Text, "Favorites", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n.Text, "Favourites", StringComparison.OrdinalIgnoreCase)));
            if (anchor == null) return new(Array.Empty<FavoritePair>(), false, "Open Favorites; no visible Favorites anchor");
            string parent = anchor.Path.Contains('/') ? anchor.Path[..anchor.Path.LastIndexOf('/')] : "";
            root = all.GetValueOrDefault(parent);
        }
        if (root == null) return new(Array.Empty<FavoritePair>(), false, "Bind the Favorites container in UI Inspector");
        Currency Identity(UiNode n) => byMeta.GetValueOrDefault(n.Metadata ?? "")
            ?? byName.GetValueOrDefault((n.Text ?? "").Trim());
        var candidates = new List<FavoritePair>();
        foreach (var row in root.Walk().OrderByDescending(n => n.Path.Length))
        {
            if (!row.Visible || row.Children.Count < 2 || row.Height > 130 || row.Height <= 0) continue;
            var cells = row.Walk().Where(n => n.Visible).Select(n => (Node: n, Currency: Identity(n)))
                .Where(x => x.Currency != null).GroupBy(x => x.Currency.Metadata)
                .Select(g => g.First()).OrderBy(x => x.Node.X).ToArray();
            if (cells.Length != 2 || cells[1].Node.X - cells[0].Node.X < 15
                || Math.Abs(cells[0].Node.Y - cells[1].Node.Y) > 25) continue;
            var pair = new FavoritePair(cells[0].Currency, cells[1].Currency, row.Path,
                wantOnLeftConfirmed && !string.IsNullOrWhiteSpace(rootPath));
            if (pair.Want.Metadata == pair.Have.Metadata) continue;
            if (!candidates.Any(p => p.Key == pair.Key)) candidates.Add(pair);
        }
        return new(candidates, candidates.Count > 0 && candidates.All(p => p.Verified),
            candidates.Count == 0 ? "Favorites found but no two-identity rows. Dump UI; no OCR or invented pairs used."
                : candidates.All(p => p.Verified) ? "Bound Favorites container, metadata/name identities, validated left WANT/right HAVE"
                : "Candidate pairs only: bind Favorites container and verify column direction before execution");
    }
}
