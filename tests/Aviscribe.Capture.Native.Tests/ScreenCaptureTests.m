// These tests use real Core Video buffers without requesting screen access.
// Including the implementation also compiles all ScreenCaptureKit API calls.
#import "../../src/Aviscribe.Capture/Native/ScreenCapture.m"
#include <assert.h>

int main(void) {
    @autoreleasepool {
        NSDictionary *attributes = @{ (__bridge NSString *)kCVPixelBufferBytesPerRowAlignmentKey: @64 };
        CVPixelBufferRef buffer = NULL;
        assert(CVPixelBufferCreate(kCFAllocatorDefault, 3, 2, kCVPixelFormatType_32BGRA,
            (__bridge CFDictionaryRef)attributes, &buffer) == kCVReturnSuccess);
        assert(CVPixelBufferLockBaseAddress(buffer, 0) == kCVReturnSuccess);
        size_t rowBytes = CVPixelBufferGetBytesPerRow(buffer);
        assert(rowBytes > 3 * 4); // Exercise padded rows, not just packed pixels.
        uint8_t *source = CVPixelBufferGetBaseAddress(buffer);
        memset(source, 0, rowBytes * 2);
        source[0] = 10; source[1] = 20; source[2] = 30; source[3] = 255;
        source[rowBytes] = 40; source[rowBytes + 3] = 255;
        CVPixelBufferUnlockBaseAddress(buffer, 0);

        CMVideoFormatDescriptionRef format = NULL;
        assert(CMVideoFormatDescriptionCreateForImageBuffer(kCFAllocatorDefault, buffer, &format) == noErr);
        CMSampleTimingInfo timing = { CMTimeMake(1, 60), kCMTimeZero, kCMTimeInvalid };
        CMSampleBufferRef sample = NULL;
        assert(CMSampleBufferCreateReadyWithImageBuffer(kCFAllocatorDefault, buffer, format, &timing, &sample) == noErr);
        CFArrayRef attachments = CMSampleBufferGetSampleAttachmentsArray(sample, true);
        CFMutableDictionaryRef attachment = (CFMutableDictionaryRef)CFArrayGetValueAtIndex(attachments, 0);
        CFDictionarySetValue(attachment, (__bridge const void *)SCStreamFrameInfoStatus,
            (__bridge const void *)@(SCFrameStatusComplete));

        AviscribeScreenSession *session = [AviscribeScreenSession new];
        void *handle = (__bridge_retained void *)session;
        SCStream *unusedStream = nil;
        [session stream:unusedStream didOutputSampleBuffer:sample ofType:SCStreamOutputTypeScreen];

        // The bridge must copy pixels while the native buffer is locked.
        // Changing the original buffer afterward must not corrupt its frame.
        CVPixelBufferLockBaseAddress(buffer, 0);
        memset(CVPixelBufferGetBaseAddress(buffer), 99, rowBytes * 2);
        CVPixelBufferUnlockBaseAddress(buffer, 0);
        CFDictionarySetValue(attachment, (__bridge const void *)SCStreamFrameInfoStatus,
            (__bridge const void *)@(SCFrameStatusIdle));
        [session stream:unusedStream didOutputSampleBuffer:sample ofType:SCStreamOutputTypeScreen];
        CFRelease(sample);
        CFRelease(format);
        CVPixelBufferRelease(buffer);

        void *pixels = NULL;
        int width = 0, height = 0, stride = 0;
        char error[256] = {0};
        assert(aviscribe_screen_read(handle, &pixels, &width, &height, &stride, error, sizeof(error)) == 1);
        assert(width == 3 && height == 2 && stride == (int)rowBytes);
        assert(((uint8_t *)pixels)[0] == 10 && ((uint8_t *)pixels)[1] == 20);
        assert(((uint8_t *)pixels)[2] == 30 && ((uint8_t *)pixels)[stride] == 40);
        aviscribe_screen_free(pixels);

        // A stopped stream must surface its error even if a last frame exists.
        NSError *failure = [NSError errorWithDomain:@"Test" code:1
            userInfo:@{NSLocalizedDescriptionKey: @"Window closed"}];
        [session stream:unusedStream didStopWithError:failure];
        assert(aviscribe_screen_read(handle, &pixels, &width, &height, &stride, error, sizeof(error)) == 0);
        assert(pixels == NULL && strcmp(error, "Window closed") == 0);
        aviscribe_screen_close(handle);
        puts("ScreenCaptureKit bridge tests passed (padded BGRA rows, ownership, idle frames, stream errors).");
    }
    return 0;
}
