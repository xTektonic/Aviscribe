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
        public List<Moon> Moons { get; private set; }

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

        public List<Moon> GetByKingdom(string kingdom)
        {
            return Moons
                .Where(m => m.Kingdom.Equals(kingdom, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
