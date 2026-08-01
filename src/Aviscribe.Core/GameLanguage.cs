using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace Aviscribe.Core
{

    public enum GameLanguage
    {
        English,
        ChineseTraditional,
        ChineseSimplified,
        Japanese,
        Korean,
        Dutch,
        FrenchCanada,
        FrenchFrance,
        German,
        Italian,
        SpanishSpain,
        SpanishLatinAmerica,
        Russian
    }

    public static class GameLanguageCatalog
    {
        public static bool IsSupportedInputLanguage(GameLanguage language)
        {
            return language == GameLanguage.ChineseTraditional;
        }
    }
}
