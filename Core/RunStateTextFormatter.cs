using System.Linq;

namespace Aviscribe.Core
{
    public static class RunStateTextFormatter
    {
        public static string FormatPending(GameStateSnapshot snapshot)
        {
            return FormatPending(snapshot, snapshot.OutputLanguage);
        }

        public static string FormatPending(GameStateSnapshot snapshot, GameLanguage language)
        {
            return string.Join(
                System.Environment.NewLine,
                snapshot.Pending.Select(moon => MoonDisplay.Format(moon, language)));
        }
    }
}
