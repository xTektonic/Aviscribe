#import <Foundation/Foundation.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>
#include <math.h>

// Only these four C functions cross into .NET. Objective-C delegates and blocks
// stay under ARC; no managed callback can be collected while macOS uses it.
#define EXPORT __attribute__((visibility("default")))

@interface AviscribeScreenSession : NSObject <SCStreamOutput, SCStreamDelegate>
@property(nonatomic, strong) NSCondition *condition;
@property(nonatomic, strong) dispatch_queue_t controlQueue;
@property(nonatomic, strong) dispatch_queue_t frameQueue;
@property(nonatomic, strong) SCStream *stream;
@property(nonatomic, strong) NSData *pixels;
@property(nonatomic, copy) NSString *failure;
@property(nonatomic) int width;
@property(nonatomic) int height;
@property(nonatomic) int stride;
@property(nonatomic) BOOL closed;
@property(nonatomic) BOOL starting;
- (void)begin:(uint32_t)windowID;
- (void)close;
@end

@implementation AviscribeScreenSession
- (instancetype)init {
    if ((self = [super init])) {
        _condition = [NSCondition new];
        _controlQueue = dispatch_queue_create("io.github.xtektonic.aviscribe.screen-control", DISPATCH_QUEUE_SERIAL);
        _frameQueue = dispatch_queue_create("io.github.xtektonic.aviscribe.screen-frames", DISPATCH_QUEUE_SERIAL);
    }
    return self;
}

- (void)fail:(NSString *)message {
    [self.condition lock];
    self.failure = message;
    [self.condition broadcast];
    [self.condition unlock];
}

- (BOOL)isClosed {
    [self.condition lock];
    BOOL result = self.closed;
    [self.condition unlock];
    return result;
}

- (void)stopStream {
    // Called on controlQueue. Retain the session until stop finishes so that
    // in-flight frame/delegate callbacks never target a released object.
    SCStream *stream = self.stream;
    self.stream = nil;
    if (stream) {
        [stream stopCaptureWithCompletionHandler:^(NSError *error) {
            NSError *removeError = nil;
            [stream removeStreamOutput:self type:SCStreamOutputTypeScreen error:&removeError];
        }];
    }
}

- (void)begin:(uint32_t)windowID {
    [SCShareableContent getShareableContentExcludingDesktopWindows:YES
                                            onScreenWindowsOnly:NO
                                              completionHandler:^(SCShareableContent *content, NSError *error) {
        dispatch_async(self.controlQueue, ^{
            @autoreleasepool {
                if ([self isClosed]) return;
                if (error) {
                    [self fail:[NSString stringWithFormat:@"ScreenCaptureKit: %@. Allow Aviscribe in Screen & System Audio Recording, then restart Aviscribe.", error.localizedDescription]];
                    return;
                }
                SCWindow *selected = nil;
                for (SCWindow *window in content.windows) {
                    if (window.windowID == windowID) { selected = window; break; }
                }
                if (!selected) {
                    [self fail:@"The selected window is no longer shareable. Refresh the source list and select it again."];
                    return;
                }
                SCContentFilter *filter = [[SCContentFilter alloc] initWithDesktopIndependentWindow:selected];
                SCStreamConfiguration *configuration = [SCStreamConfiguration new];
                // Capture at Retina resolution with a bound on memory use.
                double scale = filter.pointPixelScale;
                double width = ceil(filter.contentRect.size.width * scale);
                double height = ceil(filter.contentRect.size.height * scale);
                if (!isfinite(width) || !isfinite(height) || width < 1 || height < 1) {
                    [self fail:@"The selected window has no drawable area."];
                    return;
                }
                double reduction = fmin(1.0, 4096.0 / fmax(width, height));
                configuration.width = (size_t)fmax(1, floor(width * reduction));
                configuration.height = (size_t)fmax(1, floor(height * reduction));
                configuration.pixelFormat = kCVPixelFormatType_32BGRA;
                configuration.minimumFrameInterval = CMTimeMake(1, 60);
                configuration.queueDepth = 3;
                configuration.showsCursor = NO;
                configuration.capturesAudio = NO;
                configuration.ignoreShadowsSingleWindow = YES;
                self.stream = [[SCStream alloc] initWithFilter:filter configuration:configuration delegate:self];
                NSError *outputError = nil;
                if (![self.stream addStreamOutput:self type:SCStreamOutputTypeScreen
                               sampleHandlerQueue:self.frameQueue error:&outputError]) {
                    [self fail:outputError.localizedDescription ?: @"Could not attach the screen capture output."];
                    self.stream = nil;
                    return;
                }
                self.starting = YES;
                [self.stream startCaptureWithCompletionHandler:^(NSError *startError) {
                    dispatch_async(self.controlQueue, ^{
                        self.starting = NO;
                        if (startError) [self fail:startError.localizedDescription];
                        if ([self isClosed] || startError) [self stopStream];
                    });
                }];
            }
        });
    }];
}

- (void)stream:(SCStream *)stream didStopWithError:(NSError *)error {
    [self fail:error.localizedDescription ?: @"Screen capture stopped."];
}

- (void)stream:(SCStream *)stream didOutputSampleBuffer:(CMSampleBufferRef)sample ofType:(SCStreamOutputType)type {
    @autoreleasepool {
        if (type != SCStreamOutputTypeScreen || !CMSampleBufferIsValid(sample) || [self isClosed]) return;
        NSArray *attachments = (__bridge NSArray *)CMSampleBufferGetSampleAttachmentsArray(sample, false);
        NSNumber *status = attachments.firstObject[SCStreamFrameInfoStatus];
        // Idle frames do not contain new pixels. Preserve the last complete
        // frame so a static window remains capturable instead of timing out.
        if (!status || status.integerValue != SCFrameStatusComplete) return;
        CVPixelBufferRef buffer = CMSampleBufferGetImageBuffer(sample);
        if (!buffer || CVPixelBufferGetPixelFormatType(buffer) != kCVPixelFormatType_32BGRA) return;
        if (CVPixelBufferLockBaseAddress(buffer, kCVPixelBufferLock_ReadOnly) != kCVReturnSuccess) return;
        size_t width = CVPixelBufferGetWidth(buffer);
        size_t height = CVPixelBufferGetHeight(buffer);
        size_t stride = CVPixelBufferGetBytesPerRow(buffer);
        void *base = CVPixelBufferGetBaseAddress(buffer);
        NSData *pixels = nil;
        if (base && width > 0 && height > 0 && width <= 4096 && height <= 4096 &&
            stride >= width * 4 && stride <= INT_MAX && stride * height <= 128 * 1024 * 1024) {
            pixels = [NSData dataWithBytes:base length:stride * height];
        }
        CVPixelBufferUnlockBaseAddress(buffer, kCVPixelBufferLock_ReadOnly);
        if (!pixels) { [self fail:@"ScreenCaptureKit returned an invalid frame layout."]; return; }
        [self.condition lock];
        if (!self.closed) {
            self.pixels = pixels;
            self.width = (int)width;
            self.height = (int)height;
            self.stride = (int)stride;
            [self.condition broadcast];
        }
        [self.condition unlock];
    }
}

- (void)close {
    [self.condition lock];
    self.closed = YES;
    self.pixels = nil;
    [self.condition broadcast];
    [self.condition unlock];
    dispatch_async(self.controlQueue, ^{
        // If start is still completing, its completion performs the stop.
        if (!self.starting) [self stopStream];
    });
}
@end

EXPORT void *aviscribe_screen_open(uint32_t windowID) {
    @autoreleasepool {
        AviscribeScreenSession *session = [AviscribeScreenSession new];
        [session begin:windowID];
        return (__bridge_retained void *)session;
    }
}

// Returns an owned BGRA allocation. The caller must free it after copying.
EXPORT int aviscribe_screen_read(void *handle, void **data, int *width, int *height,
                                 int *stride, char *error, int errorCapacity) {
    @autoreleasepool {
        *data = NULL;
        AviscribeScreenSession *session = (__bridge AviscribeScreenSession *)handle;
        [session.condition lock];
        NSDate *deadline = [NSDate dateWithTimeIntervalSinceNow:5];
        while (!session.pixels && !session.failure && !session.closed) {
            if (![session.condition waitUntilDate:deadline]) break;
        }
        NSString *failure = session.failure;
        if (session.closed) failure = @"Screen capture was stopped.";
        if (!failure && !session.pixels) failure = @"No screen frame arrived. Check Screen & System Audio Recording permission and restore the selected window.";
        if (!failure) {
            *data = malloc(session.pixels.length);
            if (*data) {
                memcpy(*data, session.pixels.bytes, session.pixels.length);
                *width = session.width;
                *height = session.height;
                *stride = session.stride;
            } else failure = @"Could not allocate a screen frame.";
        }
        if (failure && errorCapacity > 0) {
            strlcpy(error, failure.UTF8String, (size_t)errorCapacity);
        }
        [session.condition unlock];
        return failure ? 0 : 1;
    }
}

EXPORT void aviscribe_screen_close(void *handle) {
    @autoreleasepool {
        if (handle) {
            AviscribeScreenSession *session = (__bridge_transfer AviscribeScreenSession *)handle;
            [session close];
        }
    }
}

EXPORT void aviscribe_screen_free(void *data) { free(data); }
