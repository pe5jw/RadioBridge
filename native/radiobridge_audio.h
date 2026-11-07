/*
 * RadioBridge Native Audio Shim
 * Thin C API over miniaudio for use from C# via P/Invoke.
 *
 * Both capture and playback run simultaneously (full duplex).
 * The C# side supplies callbacks for captured audio and to fill playback.
 */

#ifndef RADIOBRIDGE_AUDIO_H
#define RADIOBRIDGE_AUDIO_H

#ifdef _WIN32
  #define RB_EXPORT __declspec(dllexport)
#else
  #define RB_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Called when captured PCM samples are ready.
   buffer  : interleaved 16-bit PCM samples
   frames  : number of frames (samples per channel) */
typedef void (*RbCaptureCallback)(const short* buffer, int frames);

/* Called when the playback device needs audio.
   buffer  : write interleaved 16-bit PCM here
   frames  : number of frames requested */
typedef void (*RbPlaybackCallback)(short* buffer, int frames);

/* Initialise capture + playback devices.
   captureIndex   : device index (-1 = default)
   playbackIndex  : device index (-1 = default)
   sampleRate     : e.g. 48000
   channels       : 1 or 2
   bufferFrames   : requested buffer size in frames (latency hint)
   Returns 0 on success, non-zero on error. */
RB_EXPORT int rb_audio_init(
    int captureIndex, int playbackIndex,
    int sampleRate, int channels, int bufferFrames,
    RbCaptureCallback captureCallback,
    RbPlaybackCallback playbackCallback);

/* Start audio streaming. Call after rb_audio_init. */
RB_EXPORT int rb_audio_start(void);

/* Stop audio streaming. */
RB_EXPORT void rb_audio_stop(void);

/* Free all resources. Call after rb_audio_stop. */
RB_EXPORT void rb_audio_uninit(void);

/* Device enumeration.
   isCapture : 1 for capture devices, 0 for playback devices
   Returns number of devices (not counting the default). */
RB_EXPORT int rb_get_device_count(int isCapture);

/* Get device name.
   index  : 0-based device index
   name   : output buffer
   len    : size of name buffer */
RB_EXPORT void rb_get_device_name(int isCapture, int index, char* name, int len);

/* Last error message (static buffer, not thread-safe). */
RB_EXPORT const char* rb_get_last_error(void);

#ifdef __cplusplus
}
#endif

#endif /* RADIOBRIDGE_AUDIO_H */
