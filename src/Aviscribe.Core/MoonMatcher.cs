using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aviscribe.Core
{
    public class MoonMatcher
    {
        private readonly MoonRepository _repo;

        // Language of OCR input (what Talkatoo shows in-game)
        private readonly GameLanguage _inputLanguage;

        // Language you want to DISPLAY / send to OBS
        private readonly GameLanguage _outputLanguage;

        public double Threshold { get; set; } = 0.6;
        public int MaxCandidates { get; set; } = 3;

        public MoonMatcher(
            MoonRepository repo,
            GameLanguage inputLanguage,
            GameLanguage outputLanguage)
        {
            _repo = repo;
            _inputLanguage = inputLanguage;
            _outputLanguage = outputLanguage;
        }

        public MatchResult Match(string input, string kingdom)
        {
            return Match(input, _repo.GetByKingdom(kingdom));
        }

        public MatchResult MatchTalkatooText(string input, string kingdom, RunSettings settings)
        {
            return Match(input, _repo.GetTalkatooCandidates(kingdom, settings));
        }

        public MatchResult MatchCollectionText(string input, string kingdom, RunSettings settings)
        {
            return Match(input, _repo.GetCollectionCandidates(kingdom, settings));
        }

        public MatchResult Match(string input, IEnumerable<Moon> moons)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new MatchResult
                {
                    BestMatch = null,
                    Score = 0,
                    Candidates = new List<(Moon moon, double score)>()
                };
            }

            var normalizedInput = Normalize(input);

            var results = new List<(Moon moon, double score)>();

            foreach (var moon in moons)
            {
                // IMPORTANT FIX:
                // Compare OCR input language against SAME language field in JSON
                var moonText = Normalize(moon.GetName(_inputLanguage));

                if (string.IsNullOrWhiteSpace(moonText))
                    continue;

                var score = Math.Max(
                    Levenshtein.Similarity(normalizedInput, moonText),
                    LongestCommonSubstringSimilarity(normalizedInput, moonText));

                results.Add((moon, score));
            }

            var ordered = results
                .OrderByDescending(r => r.score)
                .ToList();

            var best = PreferMatchingTrailingNumber(normalizedInput, ordered.FirstOrDefault(), ordered);
            var preferredIndex = ordered.FindIndex(candidate => candidate.moon?.Id == best.moon?.Id);
            if (preferredIndex > 0)
            {
                ordered.RemoveAt(preferredIndex);
                ordered.Insert(0, best);
            }

            var ambiguous = IsAmbiguousNumberedVariant(normalizedInput, best, ordered);

            return new MatchResult
            {
                BestMatch = best.score >= Threshold && !ambiguous ? best.moon : null,
                Score = best.score,
                IsAmbiguous = ambiguous,
                Candidates = ordered.Take(MaxCandidates).ToList()
            };
        }

        private (Moon moon, double score) PreferMatchingTrailingNumber(
            string normalizedInput,
            (Moon moon, double score) best,
            IReadOnlyList<(Moon moon, double score)> ordered)
        {
            var inputNumber = GetTrailingArabicNumber(normalizedInput);
            if (best.moon == null || best.score < Threshold || inputNumber == null)
                return best;

            var bestName = Normalize(best.moon.GetName(_inputLanguage));
            var bestBase = StripTrailingNumber(bestName);
            if (bestBase == bestName || string.IsNullOrWhiteSpace(bestBase))
                return best;

            var matchingVariants = ordered
                .Take(8)
                .Where(candidate =>
                    candidate.score >= Threshold &&
                    best.score - candidate.score <= 0.06 &&
                    StripTrailingNumber(Normalize(candidate.moon.GetName(_inputLanguage))) == bestBase &&
                    GetTrailingArabicNumber(Normalize(candidate.moon.GetName(_inputLanguage))) == inputNumber)
                .ToList();

            return matchingVariants.Count == 1 ? matchingVariants[0] : best;
        }

        private bool IsAmbiguousNumberedVariant(
            string normalizedInput,
            (Moon moon, double score) best,
            IReadOnlyList<(Moon moon, double score)> ordered)
        {
            if (best.moon == null || best.score < Threshold)
                return false;

            var bestName = Normalize(best.moon.GetName(_inputLanguage));
            var bestBase = StripTrailingNumber(bestName);
            if (bestBase == bestName || string.IsNullOrWhiteSpace(bestBase))
                return false;

            var numberedVariants = ordered
                .Take(8)
                .Where(candidate =>
                    candidate.score >= Threshold &&
                    best.score - candidate.score <= 0.06 &&
                    StripTrailingNumber(Normalize(candidate.moon.GetName(_inputLanguage))) == bestBase &&
                    GetTrailingArabicNumber(Normalize(candidate.moon.GetName(_inputLanguage))) != null)
                .ToList();

            if (numberedVariants.Select(candidate => candidate.moon.Id).Distinct().Count() < 2)
                return false;

            var inputNumber = GetTrailingArabicNumber(normalizedInput);
            return inputNumber == null ||
                   numberedVariants.Count(candidate =>
                       GetTrailingArabicNumber(Normalize(candidate.moon.GetName(_inputLanguage))) == inputNumber) != 1;
        }

        private static string StripTrailingNumber(string input)
        {
            return Regex.Replace(input, @"[0-9]+$", string.Empty);
        }

        private static string? GetTrailingArabicNumber(string input)
        {
            var match = Regex.Match(input, @"[0-9]+$");
            return match.Success ? match.Value : null;
        }

        public string GetDisplayName(Moon? moon)
        {
            if (moon == null)
                return string.Empty;

            return moon.GetName(_outputLanguage) ?? moon.English;
        }

        public string GetBestMatchDisplayName(string input, string kingdom)
        {
            var result = Match(input, kingdom);
            return GetDisplayName(result.BestMatch);
        }

        private string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var normalized = input
                .ToLowerInvariant()
                .Replace("！", "!")
                .Replace("’", "'")
                .Replace("。", "")
                .Replace("宫", "宮")
                .Replace("髅", "髏")
                .Replace("撃", "擊")
                .Replace("击", "擊")
                .Replace("结", "結")
                .Replace("目", "月")
                .Replace(" ", "")
                .Trim();

            return string.Concat(normalized.Select(character =>
                character is >= '０' and <= '９'
                    ? (char)('0' + character - '０')
                    : character));
        }

        private static double LongestCommonSubstringSimilarity(string input, string moonText)
        {
            if (input.Length < 4 || moonText.Length < 4)
                return 0;

            var longest = 0;
            var lengths = new int[input.Length + 1, moonText.Length + 1];

            for (var i = 1; i <= input.Length; i++)
            {
                for (var j = 1; j <= moonText.Length; j++)
                {
                    if (input[i - 1] != moonText[j - 1])
                        continue;

                    var length = lengths[i - 1, j - 1] + 1;
                    lengths[i, j] = length;
                    longest = Math.Max(longest, length);
                }
            }

            if (longest < 4)
                return 0;

            var shorter = Math.Min(input.Length, moonText.Length);
            return longest / (double)Math.Max(1, shorter);
        }
    }
}
