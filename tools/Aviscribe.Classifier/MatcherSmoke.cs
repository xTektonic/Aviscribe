using Aviscribe.Core;

namespace Aviscribe.Classifier
{
    internal static class MatcherSmoke
    {
        public static void Run()
        {
            KeepsNumberedVariantsAmbiguous();
            MatchesNoisyChineseSubstrings();
            Console.WriteLine("Matcher smoke passed.");
        }

        private static void KeepsNumberedVariantsAmbiguous()
        {
            var repo = new MoonRepository();
            repo.Moons.AddRange(
            [
                new Moon { Id = 1, Kingdom = "Cascade", English = "Cascade Timer Challenge 1" },
                new Moon { Id = 2, Kingdom = "Cascade", English = "Cascade Timer Challenge 2" },
                new Moon { Id = 3, Kingdom = "Cascade", English = "Behind the Waterfall" },
            ]);
            var settings = new RunSettings { InputLanguage = GameLanguage.English };
            var matcher = new MoonMatcher(repo, GameLanguage.English);

            var result = matcher.MatchTalkatooText("Cascade Timer Challenge", "Cascade", settings);

            Expect(result.IsAmbiguous, "Numbered variants should remain ambiguous.");
            Expect(result.BestMatch == null, "Ambiguous numbered variants should not auto-select a best match.");
        }

        private static void MatchesNoisyChineseSubstrings()
        {
            var repo = new MoonRepository();
            repo.Moons.AddRange(
            [
                new Moon
                {
                    Id = 1,
                    Kingdom = "Cascade",
                    English = "Our First Power Moon",
                    ChineseTraditional = "第一個 力量之月"
                },
                new Moon
                {
                    Id = 2,
                    Kingdom = "Cascade",
                    English = "Multi Moon Atop the Falls",
                    ChineseTraditional = "瀑布上的崇高之月"
                },
            ]);
            var settings = new RunSettings { InputLanguage = GameLanguage.ChineseTraditional };
            var matcher = new MoonMatcher(repo, GameLanguage.ChineseTraditional);

            var result = matcher.MatchCollectionText("烟力量之目", "Cascade", settings);

            Expect(result.BestMatch?.Id == 1, "Noisy OCR substring should match the first power moon.");
            Expect(!result.IsAmbiguous, "Noisy OCR substring should not become ambiguous when only one candidate fits.");
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
