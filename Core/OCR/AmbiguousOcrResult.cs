using System.Collections.Generic;
using System.Linq;

namespace Aviscribe.Core.Ocr
{
    public sealed class AmbiguousOcrResult
    {
        public AmbiguousOcrResult(OcrRegionType type, string text, IEnumerable<(Moon moon, double score)> candidates)
        {
            Type = type;
            Text = text;
            Candidates = candidates
                .Select(candidate => new OcrMatchCandidate(candidate.moon, candidate.score))
                .ToList();
        }

        public OcrRegionType Type { get; }
        public string Text { get; }
        public IReadOnlyList<OcrMatchCandidate> Candidates { get; }
    }

    public sealed record OcrMatchCandidate(Moon Moon, double Score);
}
