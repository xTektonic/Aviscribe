using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aviscribe.Core
{
    public class MatchResult
    {
        public Moon BestMatch { get; set; }
        public double Score { get; set; }

        public List<(Moon moon, double score)> Candidates { get; set; } = new();
    }
}
