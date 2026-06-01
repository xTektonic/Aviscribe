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

                var score = Levenshtein.Similarity(normalizedInput, moonText);

                results.Add((moon, score));
            }

            var ordered = results
                .OrderByDescending(r => r.score)
                .ToList();

            var best = ordered.FirstOrDefault();
            var ambiguous = IsAmbiguousNumberedVariant(best, ordered);

            return new MatchResult
            {
                BestMatch = best.score >= Threshold && !ambiguous ? best.moon : null,
                Score = best.score,
                IsAmbiguous = ambiguous,
                Candidates = ordered.Take(MaxCandidates).ToList()
            };
        }

        private bool IsAmbiguousNumberedVariant((Moon moon, double score) best, IReadOnlyList<(Moon moon, double score)> ordered)
        {
            if (best.moon == null || best.score < Threshold)
                return false;

            var bestBase = StripTrailingNumber(Normalize(best.moon.GetName(_inputLanguage)));
            if (string.IsNullOrWhiteSpace(bestBase))
                return false;

            return ordered
                .Skip(1)
                .Take(8)
                .Any(candidate =>
                    candidate.score >= Threshold &&
                    best.score - candidate.score <= 0.06 &&
                    StripTrailingNumber(Normalize(candidate.moon.GetName(_inputLanguage))) == bestBase &&
                    candidate.moon.Id != best.moon.Id);
        }

        private static string StripTrailingNumber(string input)
        {
            return Regex.Replace(input, @"[\d０-９]+$", string.Empty);
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

            return input
                .ToLowerInvariant()
                .Replace("！", "!")
                .Replace("’", "'")
                .Replace("。", "")
                .Replace(" ", "")
                .Trim();
        }
    }
}
