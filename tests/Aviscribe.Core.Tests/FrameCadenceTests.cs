using Aviscribe.Core.Ocr;

namespace Aviscribe.Core.Tests;

public sealed class FrameCadenceTests
{
    [Fact]
    public void TalkatooInspectionCadenceUsesDeliveredFrames()
    {
        var tracker = new TalkatooConfirmationTracker();

        var inspected = Enumerable.Range(1, 30)
            .Where(frame => tracker.ShouldInspect(frame))
            .ToArray();

        Assert.Equal([1, 7, 13, 19, 25], inspected);
    }
}
