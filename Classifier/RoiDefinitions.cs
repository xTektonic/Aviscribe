using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class RoiDefinitions
    {
        public static readonly (string Name, Rect Bounds)[] StandardTextRegions =
        [
            ("Talkatoo", new Rect(666, 862, 649, 48)),
            ("MoonGet", new Rect(490, 797, 930, 60))
        ];
    }
}
