using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Starter loading tips made from what's on the map (its perks and their prices, wall buys, power, box spots, rocks,
    /// starting gun), and a check for tips whose prices no longer match the map. SPZombies already shows its own general
    /// tips (power-ups, what each perk does, the box, Pack-a-Punch) with every map, so these are only about this map.
    /// </summary>
    internal static class KitTips
    {
        /// <summary>The starter tips for the open map, from its markers and settings.</summary>
        public static List<string> Starter(ZombiesMapSettings s)
        {
            var tips = new List<string>();
            List<ZombiesMarker> markers = KitExport.InOrder<ZombiesMarker>();
            List<string> perks = markers.Where(m => m.Kind == ZombiesKind.PerkMachine && !string.IsNullOrEmpty(m.What))
                .Select(m => m.What).Distinct().ToList();
            bool pap = markers.Any(m => m.Kind == ZombiesKind.PackAPunch);
            bool powerSwitch = markers.Any(m => m.Kind == ZombiesKind.PowerSwitch);

            if (!string.IsNullOrEmpty(s.StartGun) && s.StartGun != KitInfo.DefaultStartGun)
                tips.Add($"On this map you start with the {KitInstall.GunName(s.StartGun)}.");
            foreach (string id in KitInfo.PerkIds.Where(perks.Contains))
            {
                if (id == "revive")
                    tips.Add($"Quick Revive costs {Price(s, id)} points in co-op. Playing solo it costs 500, then 1500, then 2500.");
                else
                    tips.Add($"{KitInfo.PerkGameName(id)} costs {Price(s, id)} points.");
            }
            int max = s.MaxPerks > 0 ? s.MaxPerks : 4;
            if (max != 4 && perks.Count > 0)
                tips.Add($"On this map you can hold up to {max} perks.");
            if (s.AlwaysPowered)
            {
                if (perks.Count > 0 || pap)
                    tips.Add("The power is on from the start: every perk machine works right away.");
            }
            else if (powerSwitch && (perks.Count > 0 || pap))
                tips.Add($"Find the power switch and turn it on: the perk machines{(pap ? " and Pack-a-Punch" : "")} won't work until you do.");
            if (!s.AlwaysPowered && markers.Any(m => (m.Kind == ZombiesKind.Door || m.Kind == ZombiesKind.Debris) && m.OpensWithPower))
                tips.Add("Some doors can't be bought: they open when the power comes on.");
            foreach (var g in markers.Where(m => m.Kind == ZombiesKind.WallBuy && !string.IsNullOrEmpty(m.What)).GroupBy(m => m.What))
            {
                int price = g.Min(m => m.Price);
                tips.Add(g.Key == KitInfo.GrenadesWallId
                    ? $"Grenades can be bought off a wall for {price} points."
                    : $"The {KitInstall.GunName(g.Key)} can be bought off a wall for {price} points.");
            }
            int boxSpots = markers.Count(m => m.Kind == ZombiesKind.BoxSpot);
            if (boxSpots > 1)
                tips.Add($"The mystery box can turn up in {boxSpots} places on this map.");
            int rocks = markers.Count(m => m.Kind == ZombiesKind.Rock && m.What != "lore");
            if (rocks > 0)
                tips.Add($"{rocks} glowing 115 rock{(rocks == 1 ? " is" : "s are")} hidden on this map. Find {(rocks == 1 ? "it" : "them all")} and listen...");
            return tips;
        }

        private static int Price(ZombiesMapSettings s, string perk)
        {
            int p = s.PriceOf(perk);
            return p > 0 ? p : KitInfo.PerkDefaultPrice(perk);
        }

        /// <summary>The tips the map doesn't have yet (the same text, ignoring case and spaces at the ends).</summary>
        public static List<string> Missing(ZombiesMapSettings s)
        {
            var have = new HashSet<string>(Lines(s.Tips).Select(l => l.ToLowerInvariant()));
            return Starter(s).Where(t => !have.Contains(t.ToLowerInvariant())).ToList();
        }

        public static List<string> Lines(string tips) =>
            (tips ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();

        private static readonly Regex Number = new Regex(@"\d[\d,]*", RegexOptions.Compiled);

        /// <summary>
        /// Tips naming a perk or a wall gun with a price that isn't its price on this map (e.g. a starter tip left over
        /// from before a price change): each with what the price is now.
        /// </summary>
        public static List<string> WrongPrices(ZombiesMapSettings s)
        {
            var wrong = new List<string>();
            List<ZombiesMarker> walls = KitExport.InOrder<ZombiesMarker>().Where(m => m.Kind == ZombiesKind.WallBuy && !string.IsNullOrEmpty(m.What)).ToList();
            foreach (string line in Lines(s.Tips))
            {
                var numbers = Number.Matches(line).Cast<Match>()
                    .Select(m => int.TryParse(m.Value.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1)
                    .Where(n => n >= 100).ToList();
                if (numbers.Count == 0)
                    continue;
                string lower = line.ToLowerInvariant();
                foreach (string id in KitInfo.PerkIds)
                {
                    if (!Names(id).Any(n => lower.Contains(n)))
                        continue;
                    int price = Price(s, id);
                    // Quick Revive's solo prices (500 / 1500 / 2500) are fixed: only its co-op price can be wrong
                    bool ok = id == "revive" ? numbers.Contains(price) || !lower.Contains("co-op") : numbers.All(n => n == price);
                    if (!ok)
                        wrong.Add($"\"{line}\": {KitInfo.PerkGameName(id)} costs {price} on this map.");
                }
                foreach (var g in walls.GroupBy(m => m.What))
                {
                    string gun = KitInstall.GunName(g.Key).ToLowerInvariant();
                    if (gun.Length < 3 || !lower.Contains(gun))
                        continue;
                    var prices = g.Select(m => m.Price).Distinct().ToList();
                    if (!numbers.Any(prices.Contains))
                        wrong.Add($"\"{line}\": the {KitInstall.GunName(g.Key)} wall buy costs {string.Join(" / ", prices)} on this map.");
                }
            }
            return wrong;
        }

        private static IEnumerable<string> Names(string id)
        {
            yield return KitInfo.PerkName(id).ToLowerInvariant();
            yield return KitInfo.PerkGameName(id).ToLowerInvariant();
        }
    }
}
