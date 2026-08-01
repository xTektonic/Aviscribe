using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Aviscribe.Core
{
    public class MoonRepository
    {
        private static readonly HashSet<string> PostGameKingdoms = new(StringComparer.OrdinalIgnoreCase)
        {
            "Mushroom",
            "Dark",
            "Darker"
        };

        public List<Moon> Moons { get; private set; } = new();

        public static MoonRepository Load(string path)
        {
            var json = File.ReadAllText(path);

            var moons = JsonSerializer.Deserialize<List<Moon>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return new MoonRepository
            {
                Moons = moons ?? new List<Moon>()
            };
        }

        public static MoonRepository LoadDefault()
        {
            return Load(AppPaths.MoonList);
        }

        public bool IsPostGameKingdom(string kingdom)
        {
            return PostGameKingdoms.Contains(kingdom);
        }

        public List<string> GetKingdoms(bool includePostGameKingdoms)
        {
            return Moons
                .Select(moon => moon.Kingdom)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(kingdom => includePostGameKingdoms || !IsPostGameKingdom(kingdom))
                .ToList();
        }

        public List<string> GetKingdoms(RunSettings settings)
        {
            var kingdoms = GetKingdoms(settings.IncludePostGameKingdoms);

            if (!settings.IncludePostGameKingdoms)
                kingdoms = kingdoms.Where(kingdom => KingdomRoute.GetRequirement(kingdom) > 0).ToList();

            return KingdomRoute.Order(kingdoms, settings).ToList();
        }

        public List<GameLanguage> GetAvailableLanguages()
        {
            return Enum.GetValues<GameLanguage>()
                .Where(language => Moons.Any(moon =>
                    !string.IsNullOrWhiteSpace(moon.GetName(language))))
                .ToList();
        }

        public List<Moon> GetByKingdom(string kingdom)
        {
            return Moons
                .Where(m => m.Kingdom.Equals(kingdom, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<Moon> Query(MoonQueryOptions options)
        {
            IEnumerable<Moon> query = Moons;

            if (!string.IsNullOrWhiteSpace(options.Kingdom))
            {
                query = options.MatchCollectionKingdom
                    ? query.Where(m => m.IsCollectedInKingdom(options.Kingdom))
                    : query.Where(m => m.Kingdom.Equals(options.Kingdom, StringComparison.OrdinalIgnoreCase));
            }

            if (!options.IncludeStory)
                query = query.Where(m => !m.IsStory);

            if (!options.IncludeNonStory)
                query = query.Where(m => m.IsStory);

            if (!options.IncludeHintArt)
                query = query.Where(m => !m.IsHintArt);

            if (!options.IncludePostGameKingdoms)
                query = query.Where(m => !PostGameKingdoms.Contains(m.Kingdom));

            return query.ToList();
        }

        public List<Moon> GetTalkatooCandidates(string kingdom, RunSettings settings)
        {
            return Query(new MoonQueryOptions
            {
                Kingdom = kingdom,
                IncludeStory = false,
                IncludeNonStory = true,
                IncludeHintArt = true,
                IncludePostGameKingdoms = settings.IncludePostGameKingdoms,
                MatchCollectionKingdom = false
            });
        }

        public List<Moon> GetCollectionCandidates(string kingdom, RunSettings settings)
        {
            return Query(new MoonQueryOptions
            {
                Kingdom = kingdom,
                IncludeStory = true,
                IncludeNonStory = true,
                IncludeHintArt = true,
                IncludePostGameKingdoms = settings.IncludePostGameKingdoms,
                MatchCollectionKingdom = true
            });
        }
    }
}
