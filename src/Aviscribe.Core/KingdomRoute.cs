using System;
using System.Collections.Generic;
using System.Linq;

namespace Aviscribe.Core
{
    public static class KingdomRoute
    {
        private static readonly IReadOnlyDictionary<string, int> Requirements =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cascade"] = 5,
                ["Sand"] = 16,
                ["Lake"] = 8,
                ["Wooded"] = 16,
                ["Lost"] = 10,
                ["Metro"] = 20,
                ["Snow"] = 10,
                ["Seaside"] = 10,
                ["Luncheon"] = 18,
                ["Ruined"] = 3,
                ["Bowsers"] = 8
            };

        public static int GetRequirement(string kingdom)
        {
            return Requirements.TryGetValue(kingdom, out var requirement) ? requirement : 0;
        }

        public static IReadOnlyList<string> Order(IEnumerable<string> kingdoms, RunSettings settings)
        {
            var route = new List<string>
            {
                "Mushroom",
                "Cap",
                "Cascade",
                "Sand",
                settings.WoodedBeforeLake ? "Wooded" : "Lake",
                settings.WoodedBeforeLake ? "Lake" : "Wooded",
                "Cloud",
                "Lost",
                "Metro",
                settings.SeasideBeforeSnow ? "Seaside" : "Snow",
                settings.SeasideBeforeSnow ? "Snow" : "Seaside",
                "Luncheon",
                "Ruined",
                "Bowsers",
                "Moon",
                "Dark",
                "Darker"
            };

            var available = kingdoms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var ordered = route
                .Where(routeKingdom => available.Contains(routeKingdom, StringComparer.OrdinalIgnoreCase))
                .ToList();

            ordered.AddRange(available.Where(kingdom =>
                !ordered.Contains(kingdom, StringComparer.OrdinalIgnoreCase)));

            return ordered;
        }
    }
}
