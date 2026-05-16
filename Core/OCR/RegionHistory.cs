using OpenCvSharp;
using System.Collections.Generic;
using System.Linq;

namespace Aviscribe.Core.Ocr
{
    internal class RegionHistory
    {
        private readonly Queue<bool> _detections = new();
        private readonly Queue<Mat> _frames = new();

        public int WindowSize { get; }

        public RegionHistory(int windowSize = 10)
        {
            WindowSize = windowSize;
        }

        public void Add(bool detected, Mat frame)
        {
            _detections.Enqueue(detected);
            _frames.Enqueue(frame.Clone());

            if (_detections.Count > WindowSize)
            {
                _detections.Dequeue();

                var old = _frames.Dequeue();
                old.Dispose();
            }
        }

        public bool IsStableDetection()
        {
            return _detections.Count == WindowSize &&
                   _detections.All(x => x);
        }

        public bool IsStableImage(double threshold = 1.0)
        {
            return true; // #TODO fix

            if (_frames.Count < WindowSize)
                return false;

            var first = _frames.Peek();

            foreach (var f in _frames)
            {
                if (Cv2.Norm(first, f, NormTypes.L2) > threshold)
                    return false;
            }

            return true;
        }

        public void Clear()
        {
            foreach (var f in _frames)
                f.Dispose();

            _frames.Clear();
            _detections.Clear();
        }
    }
}