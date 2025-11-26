#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <Speech/Speech.h>
#import <stdlib.h>
#import <string.h>
#include <stdatomic.h>

typedef void (*TranscriptionCallback)(const char* requestId, const char* transcript, const char* error);

static TranscriptionCallback gTranscriptionCallback = NULL;

static AVSpeechSynthesizer *gSynth = nil;
static NSString *gVoiceLanguage = @"en-US";
static float gSpeechRate = 0.5f;
static float gSpeechPitch = 1.0f;
static float gSpeechVolume = 1.0f;

extern "C" void speakText(const char* text)
{
    @autoreleasepool {
        if (!gSynth) gSynth = [[AVSpeechSynthesizer alloc] init];

        NSString *utteranceText = [NSString stringWithUTF8String:text ?: @""];
        AVSpeechUtterance *utterance = [AVSpeechUtterance speechUtteranceWithString:utteranceText];

        if (gVoiceLanguage && gVoiceLanguage.length > 0) {
            AVSpeechSynthesisVoice *v = [AVSpeechSynthesisVoice voiceWithLanguage:gVoiceLanguage];
            if (v) utterance.voice = v;
        }

        utterance.rate = gSpeechRate;
        utterance.pitchMultiplier = gSpeechPitch;
        utterance.volume = gSpeechVolume;

        [gSynth speakUtterance:utterance];
    }
}

extern "C" void setVoice(const char* languageCode)
{
    @autoreleasepool {
        if (languageCode == NULL) return;
        gVoiceLanguage = [NSString stringWithUTF8String:languageCode];
    }
}

extern "C" void setRatePitchVolume(float rate, float pitch, float volume)
{
    gSpeechRate = rate;
    gSpeechPitch = pitch;
    gSpeechVolume = volume;

}

extern "C" const char* getAvailableVoices()
{
    @autoreleasepool {
        NSArray<AVSpeechSynthesisVoice*> *voices = [AVSpeechSynthesisVoice speechVoices];
        NSMutableArray<NSString*> *voiceInfo = [NSMutableArray arrayWithCapacity:voices.count];

        for (AVSpeechSynthesisVoice *voice in voices)
        {
            NSString *entry = [NSString stringWithFormat:@"%@|%@|%@", voice.identifier ?: @"", voice.name ?: @"", voice.language ?: @""];
            [voiceInfo addObject:entry];
        }

        NSString *joined = [voiceInfo componentsJoinedByString:@"\n"];
        const char *utf8 = [joined UTF8String];
        if (!utf8) return strdup("");
        char *dup = strdup(utf8); // caller must free
        return dup;
    }
}

extern "C" void speakWithVoice(const char* text, const char* voiceID)
{
    @autoreleasepool {
        if (!gSynth) gSynth = [[AVSpeechSynthesizer alloc] init];

        NSString *utteranceText = [NSString stringWithUTF8String:text ?: @""];
        NSString *vid = voiceID ? [NSString stringWithUTF8String:voiceID] : nil;

        AVSpeechUtterance *utterance = [AVSpeechUtterance speechUtteranceWithString:utteranceText];
        if (vid)
        {
            AVSpeechSynthesisVoice *v = [AVSpeechSynthesisVoice voiceWithIdentifier:vid];
            if (v) utterance.voice = v;
        }
        utterance.rate = gSpeechRate;
        utterance.pitchMultiplier = gSpeechPitch;
        utterance.volume = gSpeechVolume;

        [gSynth speakUtterance:utterance];
    }
}

extern "C" void freeNativeString(const char* s)
{
    if (s) free((void*)s);
}

// Async transcription from WAV bytes: write to temp file and run SFSpeechRecognizer on it.
// requestId is an opaque string (GUID) provided by Unity to correlate responses.
extern "C" void transcribeWavAsync(const uint8_t* data, int length, const char* requestId)
{
    if (gTranscriptionCallback == NULL) return;

    @autoreleasepool {
        NSString *rid = requestId ? [NSString stringWithUTF8String:requestId] : [[NSUUID UUID] UUIDString];
        NSString *tmpDir = NSTemporaryDirectory();
        NSString *fileName = [NSString stringWithFormat:@"asr_%@.wav", rid];
        NSString *filePath = [tmpDir stringByAppendingPathComponent:fileName];
        NSURL *fileURL = [NSURL fileURLWithPath:filePath];

        NSData *d = [NSData dataWithBytes:data length:(NSUInteger)length];
        NSError *writeErr = nil;
        BOOL ok = [d writeToURL:fileURL options:NSDataWritingAtomic error:&writeErr];
        if (!ok) {
            const char* cErr = strdup([[writeErr localizedDescription] UTF8String] ?: "");
            const char* cRid = strdup([rid UTF8String] ?: "");
            gTranscriptionCallback(cRid, NULL, cErr);
            return;
        }

        // Exclude from backup (best-effort)
        @try {
            [fileURL setResourceValue:@(YES) forKey:NSURLIsExcludedFromBackupKey error:&NULL];
        } @catch (NSException *ex) { /* ignore */ }

        NSLocale *locale = [NSLocale currentLocale];
        SFSpeechRecognizer *recognizer = [[SFSpeechRecognizer alloc] initWithLocale:locale];
        if (!recognizer || !recognizer.available) {
            const char* cMsg = strdup("Speech recognizer unavailable on this device/locale");
            const char* cRid = strdup([rid UTF8String] ?: "");
            gTranscriptionCallback(cRid, NULL, cMsg);
            dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
                [[NSFileManager defaultManager] removeItemAtURL:fileURL error:nil];
            });
            return;
        }

        SFSpeechURLRecognitionRequest *request = [[SFSpeechURLRecognitionRequest alloc] initWithURL:fileURL];
        request.shouldReportPartialResults = NO;
        request.taskHint = SFSpeechRecognitionTaskHintDictation;

        __block atomic_bool cleanupDone = ATOMIC_VAR_INIT(false);

        // Start exactly one recognition task
        [recognizer recognitionTaskWithRequest:request resultHandler:^(SFSpeechRecognitionResult * _Nullable result, NSError * _Nullable error) {
            if (error) {
                const char* cErr = strdup([[error localizedDescription] UTF8String] ?: "");
                const char* cRid = strdup([rid UTF8String] ?: "");
                gTranscriptionCallback(cRid, NULL, cErr);
            } else if (result && result.isFinal) {
                NSString *transcript = result.bestTranscription.formattedString ?: @"";
                const char* cTranscript = strdup([transcript UTF8String] ?: "");
                const char* cRid = strdup([rid UTF8String] ?: "");
                gTranscriptionCallback(cRid, cTranscript, NULL);
            }

            // Attempt cleanup exactly once
            bool expected = false;
            if (atomic_compare_exchange_strong(&cleanupDone, &expected, true)) {
                [[NSFileManager defaultManager] removeItemAtURL:fileURL error:nil];
            }
        }];
    }
}

extern "C" void requestSpeechAuthorization()
{
    [SFSpeechRecognizer requestAuthorization:^(SFSpeechRecognizerAuthorizationStatus status) {
        // No callback to managed side here; Unity can call this to trigger permission prompt.
    }];
}

extern "C" void registerTranscriptionCallback(TranscriptionCallback cb)
{
    gTranscriptionCallback = cb;
}

extern "C" void cleanupOldAsrTempFiles(double maxAgeSeconds)
{
    @autoreleasepool {
        NSFileManager *fm = [NSFileManager defaultManager];
        NSString *tmpDir = NSTemporaryDirectory();
        NSError *err = nil;
        NSArray<NSString*> *files = [fm contentsOfDirectoryAtPath:tmpDir error:&err];
        if (err || files == nil) return;

        NSDate *now = [NSDate date];
        for (NSString *name in files) {
            if (![name hasPrefix:@"asr_"] || ![name hasSuffix:@".wav"]) continue;
            NSString *path = [tmpDir stringByAppendingPathComponent:name];
            NSDictionary *attrs = [fm attributesOfItemAtPath:path error:nil];
            NSDate *modDate = attrs[NSFileModificationDate];
            if (!modDate) continue;
            NSTimeInterval age = [now timeIntervalSinceDate:modDate];
            if (age > maxAgeSeconds) {
                [fm removeItemAtPath:path error:nil];
            }
        }
    }
}

// Returns a strdup'd newline-separated C string listing asr_*.wav full paths in NSTemporaryDirectory
extern "C" const char* listAsrTempFiles()
{
    @autoreleasepool {
        NSFileManager *fm = [NSFileManager defaultManager];
        NSString *tmpDir = NSTemporaryDirectory();
        NSError *err = nil;
        NSArray<NSString*> *files = [fm contentsOfDirectoryAtPath:tmpDir error:&err];
        if (err || files == nil) {
            return strdup("");
        }

        NSMutableArray<NSString*> *matching = [NSMutableArray array];
        for (NSString *name in files) {
            if ([name hasPrefix:@"asr_"] && [name hasSuffix:@".wav"]) {
                NSString *full = [tmpDir stringByAppendingPathComponent:name];
                [matching addObject:full];
            }
        }
        NSString *joined = [matching componentsJoinedByString:@"\n"];
        const char *utf8 = [joined UTF8String ?: ""];
        if (!utf8) return strdup("");
        return strdup(utf8); // caller must free with freeNativeString
    }
}

// returns a malloc'd array of strdup'd strings and sets count. The caller must call freeStringArray(ptr, count)
extern "C" char** getSomeList(int* outCount) 
{
    // Implementation goes here
    @autoreleasepool {
        NSArray<NSString*> *items = @[@"item1", @"item2", @"item3"]; // Example items
        int count = (int)[items count];
        if (count == 0) { *outCount = 0; return NULL; }
        char** arr = (char**)malloc(sizeof(char*) * count);
        for (int i = 0; i < count; ++i) {
            arr[i] = strdup([items[i] UTF8String] ?: "");
        }
        *outCount = count;
        return arr;
    }
}

extern "C" void freeStringArray(char** arr, int count)
{
    if (!arr) return;
    for (int i = 0; i < count; ++i) {
        if (arr[i]) free(arr[i]);
    }
    free(arr);
}