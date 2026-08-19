using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class TalkatooConfirmationSmoke
    {
        public static void Run()
        {
            PartialTypingNeverConfirms();
            ConfirmsOnThirdConsecutiveFrame();
            InstabilityRestartsConfirmation();
            ResetsToChangedLayout();
            AllowsOnlyOneBoundedRetry();
            PendingOcrIsDeduplicated();
            UsesAllAverageHashCells();
            Console.WriteLine("Talkatoo confirmation smoke passed.");
        }

        private static void PartialTypingNeverConfirms()
        {
            var tracker = new TalkatooConfirmationTracker();

            foreach (var width in new[] { 40, 72, 104, 136, 168 })
            {
                using var partial = CreatePrompt(textLeft: 120, textWidth: width);
                AssertNoEnqueue(
                    tracker.Observe(true, TalkatooPromptSignature.Capture(partial)),
                    $"partial typing width {width}");
            }
        }

        private static void ConfirmsOnThirdConsecutiveFrame()
        {
            var tracker = new TalkatooConfirmationTracker();
            using var image = CreatePrompt(textLeft: 120, textWidth: 180);
            var signature = TalkatooPromptSignature.Capture(image);

            if (!tracker.ShouldInspect(1))
                throw new InvalidOperationException("Talkatoo idle detection did not inspect frame one.");

            AssertNoEnqueue(tracker.Observe(true, signature), "trigger frame");

            if (!tracker.ShouldInspect(2))
                throw new InvalidOperationException("Talkatoo confirmation did not switch to every-frame inspection.");

            AssertNoEnqueue(
                tracker.Observe(true, TalkatooPromptSignature.Capture(image)),
                "second stable frame");
            var decision = tracker.Observe(true, TalkatooPromptSignature.Capture(image));

            if (!decision.ShouldEnqueue || decision.Attempt != 1)
                throw new InvalidOperationException("Talkatoo did not enqueue on the third stable frame.");
        }

        private static void ResetsToChangedLayout()
        {
            var tracker = new TalkatooConfirmationTracker();
            using var first = CreatePrompt(textLeft: 120, textWidth: 180);
            using var changed = CreatePrompt(textLeft: 150, textWidth: 140);

            tracker.Observe(true, TalkatooPromptSignature.Capture(first));
            tracker.Observe(true, TalkatooPromptSignature.Capture(first));
            var firstDecision = tracker.Observe(true, TalkatooPromptSignature.Capture(first));
            tracker.RecordEnqueued(firstDecision.Generation, firstDecision.Attempt);
            tracker.RecordResolved(firstDecision.Generation);

            AssertNoEnqueue(
                tracker.Observe(true, TalkatooPromptSignature.Capture(changed)),
                "changed-layout trigger frame");
            AssertNoEnqueue(
                tracker.Observe(true, TalkatooPromptSignature.Capture(changed)),
                "changed-layout second frame");
            var changedDecision = tracker.Observe(
                true,
                TalkatooPromptSignature.Capture(changed));

            if (!changedDecision.ShouldEnqueue ||
                changedDecision.Generation == firstDecision.Generation)
            {
                throw new InvalidOperationException(
                    "Changed Talkatoo layout did not stabilize as a new prompt.");
            }

            var cadenceTracker = new TalkatooConfirmationTracker();
            var disappearedAt = DateTime.UnixEpoch;
            cadenceTracker.Observe(false, null, disappearedAt);
            if (cadenceTracker.ShouldInspect(
                    disappearedAt +
                    TalkatooConfirmationTracker.IdleDetectionInterval / 2))
                throw new InvalidOperationException(
                    "Talkatoo disappearance did not return the tracker to idle cadence.");
        }

        private static void InstabilityRestartsConfirmation()
        {
            var tracker = new TalkatooConfirmationTracker();
            using var first = CreatePrompt(textLeft: 120, textWidth: 180);
            using var changed = CreatePrompt(textLeft: 150, textWidth: 140);

            AssertNoEnqueue(
                tracker.Observe(true, TalkatooPromptSignature.Capture(first)),
                "first unstable run frame one");
            AssertNoEnqueue(
                tracker.Observe(true, TalkatooPromptSignature.Capture(first)),
                "first unstable run frame two");
            AssertNoEnqueue(
                tracker.Observe(true, TalkatooPromptSignature.Capture(changed)),
                "changed reset frame");
            AssertNoEnqueue(
                tracker.Observe(true, TalkatooPromptSignature.Capture(changed)),
                "changed run frame two");

            var decision = tracker.Observe(
                true,
                TalkatooPromptSignature.Capture(changed));
            if (!decision.ShouldEnqueue)
                throw new InvalidOperationException(
                    "Talkatoo instability did not restart a three-frame confirmation run.");
        }

        private static void AllowsOnlyOneBoundedRetry()
        {
            var tracker = new TalkatooConfirmationTracker();
            using var image = CreatePrompt(textLeft: 120, textWidth: 180);

            tracker.Observe(true, TalkatooPromptSignature.Capture(image));
            tracker.Observe(true, TalkatooPromptSignature.Capture(image));
            var first = tracker.Observe(true, TalkatooPromptSignature.Capture(image));
            tracker.RecordEnqueued(first.Generation, first.Attempt);
            tracker.RecordUnresolved(first.Generation);

            for (var additionalFrame = 1;
                 additionalFrame < TalkatooConfirmationTracker.RetryStableFrames;
                 additionalFrame++)
            {
                AssertNoEnqueue(
                    tracker.Observe(true, TalkatooPromptSignature.Capture(image)),
                    $"retry wait frame {additionalFrame}");
            }

            var retry = tracker.Observe(true, TalkatooPromptSignature.Capture(image));
            if (!retry.ShouldEnqueue || retry.Attempt != 2)
                throw new InvalidOperationException(
                    "Unresolved Talkatoo prompt did not retry after six additional stable frames.");

            tracker.RecordEnqueued(retry.Generation, retry.Attempt);
            tracker.RecordUnresolved(retry.Generation);

            for (var frame = 0; frame < 12; frame++)
            {
                AssertNoEnqueue(
                    tracker.Observe(true, TalkatooPromptSignature.Capture(image)),
                    $"post-retry frame {frame + 1}");
            }
        }

        private static void UsesAllAverageHashCells()
        {
            using var image = new Mat(new Size(8, 8), MatType.CV_8UC3, Scalar.Black);
            image.Set(0, 0, new Vec3b(255, 255, 255));
            image.Set(7, 7, new Vec3b(255, 255, 255));

            var hash = ImageHash.Compute(image);
            var expected = 1UL | (1UL << 63);
            if (hash != expected)
            {
                throw new InvalidOperationException(
                    $"Average hash did not use the complete 8x8 image: {hash:X16}.");
            }
        }

        private static void PendingOcrIsDeduplicated()
        {
            var tracker = new TalkatooConfirmationTracker();
            using var image = CreatePrompt(textLeft: 120, textWidth: 180);

            tracker.Observe(true, TalkatooPromptSignature.Capture(image));
            tracker.Observe(true, TalkatooPromptSignature.Capture(image));
            var first = tracker.Observe(true, TalkatooPromptSignature.Capture(image));
            if (!tracker.RecordEnqueued(first.Generation, first.Attempt))
                throw new InvalidOperationException("Could not latch the initial Talkatoo OCR item.");

            for (var frame = 0; frame < 20; frame++)
            {
                AssertNoEnqueue(
                    tracker.Observe(true, TalkatooPromptSignature.Capture(image)),
                    $"queued duplicate frame {frame + 1}");
            }
        }

        private static Mat CreatePrompt(int textLeft, int textWidth)
        {
            var image = new Mat(new Size(649, 48), MatType.CV_8UC3, Scalar.Black);
            Cv2.Rectangle(
                image,
                new Rect(15, 2, 52, 42),
                new Scalar(230, 230, 230),
                thickness: 2);

            for (var x = textLeft; x < textLeft + textWidth; x += 12)
            {
                Cv2.Rectangle(
                    image,
                    new Rect(x, 10, Math.Min(7, textLeft + textWidth - x), 24),
                    new Scalar(20, 210, 235),
                    thickness: -1);
            }

            return image;
        }

        private static void AssertNoEnqueue(
            TalkatooConfirmationDecision decision,
            string frameName)
        {
            if (decision.ShouldEnqueue)
                throw new InvalidOperationException(
                    $"Talkatoo enqueued unexpectedly on {frameName}.");
        }
    }
}
