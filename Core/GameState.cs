using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aviscribe.Core
{
    public class GameState
    {
        public string CurrentKingdom { get; private set; }

        public List<Moon> Pending { get; private set; } = new();
        public List<Moon> Collected { get; private set; } = new();

        public void SetKingdom(string kingdom)
        {
            if (CurrentKingdom != kingdom)
            {
                CurrentKingdom = kingdom;
                Pending.Clear();
                Collected.Clear();
            }
        }

        public void AddPending(Moon moon)
        {
            if (moon == null) return;

            if (!Pending.Contains(moon) && !Collected.Contains(moon))
            {
                Pending.Add(moon);
            }
        }

        public void MarkCollected(Moon moon)
        {
            if (moon == null) return;

            if (Pending.Remove(moon))
            {
                Collected.Add(moon);
            }
        }
    }
}
