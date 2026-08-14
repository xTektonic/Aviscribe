using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    internal sealed class TalkatooAdaptiveAnalyzer
    {
        private static readonly double[] CandidateGains = [1.05, 1.10, 1.15];

        private double? _lockedGain;
        private bool _adaptiveRunActive;

        public TalkatooAdaptiveAnalysis Analyze(Mat image)
        {
            var strictGate = TalkatooStaticGate.Analyze(image);

            if (_lockedGain is double lockedGain)
            {
                var lockedGate = AnalyzeWithGain(image, lockedGain);
                if (lockedGate.Present)
                {
                    return new TalkatooAdaptiveAnalysis(
                        lockedGate,
                        lockedGain,
                        Adapted: true,
                        StartedAdaptiveRun: false);
                }

                _lockedGain = null;
            }

            if (strictGate.Present)
            {
                return new TalkatooAdaptiveAnalysis(
                    strictGate,
                    Gain: 1.0,
                    Adapted: false,
                    StartedAdaptiveRun: false);
            }

            foreach (var gain in CandidateGains)
            {
                var gate = AnalyzeWithGain(image, gain);
                if (!gate.Present)
                    continue;

                _lockedGain = gain;
                var startedAdaptiveRun = !_adaptiveRunActive;
                _adaptiveRunActive = true;
                return new TalkatooAdaptiveAnalysis(
                    gate,
                    gain,
                    Adapted: true,
                    StartedAdaptiveRun: startedAdaptiveRun);
            }

            _adaptiveRunActive = false;
            return default;
        }

        public void Reset()
        {
            _lockedGain = null;
            _adaptiveRunActive = false;
        }

        internal double? LockedGain => _lockedGain;

        private static TalkatooGateResult AnalyzeWithGain(Mat image, double gain)
        {
            using var adjusted = new Mat();
            image.ConvertTo(adjusted, image.Type(), gain);
            return TalkatooStaticGate.Analyze(adjusted);
        }
    }

    internal readonly record struct TalkatooAdaptiveAnalysis(
        TalkatooGateResult Gate,
        double Gain,
        bool Adapted,
        bool StartedAdaptiveRun)
    {
        public bool Present => Gate.Present;
    }
}
