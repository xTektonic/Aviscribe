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
            return FormatPending(snapshot, language, null);
        }

        public static string FormatPending(
            GameStateSnapshot snapshot,
            GameLanguage language,
            Func<Moon, bool>? include)
        {
            return string.Join(
                System.Environment.NewLine,
                snapshot.Pending
                    .Where(moon => include?.Invoke(moon) ?? true)
                    .Select(moon => MoonDisplay.Format(moon, language)));
        }
    }
}
