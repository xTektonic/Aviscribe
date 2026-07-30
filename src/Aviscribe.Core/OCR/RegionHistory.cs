using OpenCvSharp;
using System.Collections.Generic;
using System.Linq;

namespace Aviscribe.Core.Ocr
{
    internal class RegionHistory
    {
        private readonly Queue<bool> _detections = new();
        private readonly Queue<ulong> _hashes = new();

        public int WindowSize { get; }

        public RegionHistory(int windowSize = 10)
        {
            WindowSize = windowSize;
        }

        public void Add(bool detected, Mat frame)
        {
            _detections.Enqueue(detected);
            _hashes.Enqueue(detected ? ImageHash.Compute(frame) : 0);

            if (_detections.Count > WindowSize)
            {
                _detections.Dequeue();
                _hashes.Dequeue();
            }
        }

        public bool IsStableDetection()
        {
            return _detections.Count == WindowSize &&
                   _detections.All(x => x);
        }

        public bool IsStableImage(int maxHammingDistance = 12)
        {
            if (_hashes.Count < WindowSize)
                return false;

            var first = _hashes.Peek();

            foreach (var hash in _hashes)
            {
                if (ImageHash.Hamming(first, hash) > maxHammingDistance)
                    return false;
            }

            return true;
        }

        public void Clear()
        {
            _hashes.Clear();
            _detections.Clear();
        }
    }
}
