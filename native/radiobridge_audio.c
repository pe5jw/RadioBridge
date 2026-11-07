/*
 * RadioBridge Native Audio Shim — miniaudio implementation
 * Provides a simple C API for C# P/Invoke.
 */

#define MINIAUDIO_IMPLEMENTATION
#define MA_NO_FLAC
#define MA_NO_MP3
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#include "miniaudio.h"
#include "radiobridge_audio.h"

#include <string.h>
#include <stdio.h>
#include <stdlib.h>

/* ── State ───────────────────────────────────────────────────────────────── */

static ma_device         s_device;
static ma_device_config  s_config;
static ma_context        s_context;
static int               s_initialized = 0;
static int               s_started     = 0;

static RbCaptureCallback  s_captureCallback  = NULL;
static RbPlaybackCallback s_playbackCallback = NULL;

static char s_lastError[256] = {0};

/* ── Helpers ─────────────────────────────────────────────────────────────── */

static void set_error(const char* msg)
{
    strncpy(s_lastError, msg, sizeof(s_lastError) - 1);
    s_lastError[sizeof(s_lastError) - 1] = '\0';
}

/* ── Audio callback ──────────────────────────────────────────────────────── */

static void data_callback(
    ma_device* pDevice,
    void*       pOutput,
    const void* pInput,
    ma_uint32   frameCount)
{
    (void)pDevice;

    /* Capture: PCM from microphone → C# */
    if (pInput != NULL && s_captureCallback != NULL)
        s_captureCallback((const short*)pInput, (int)frameCount);

    /* Playback: C# fills the output buffer */
    if (pOutput != NULL)
    {
        if (s_playbackCallback != NULL)
            s_playbackCallback((short*)pOutput, (int)frameCount);
        else
            memset(pOutput, 0, frameCount * sizeof(short) * pDevice->playback.channels);
    }
}

/* ── Init ────────────────────────────────────────────────────────────────── */

RB_EXPORT int rb_audio_init(
    int captureIndex, int playbackIndex,
    int sampleRate, int channels, int bufferFrames,
    RbCaptureCallback captureCallback,
    RbPlaybackCallback playbackCallback)
{
    if (s_initialized)
        rb_audio_uninit();

    s_captureCallback  = captureCallback;
    s_playbackCallback = playbackCallback;

    /* Init context to enumerate devices */
    if (ma_context_init(NULL, 0, NULL, &s_context) != MA_SUCCESS)
    {
        set_error("ma_context_init mislukt");
        return -1;
    }

    /* Resolve device IDs */
    ma_device_id* pCaptureId  = NULL;
    ma_device_id* pPlaybackId = NULL;
    ma_device_info* pCapDevices  = NULL;
    ma_device_info* pPlayDevices = NULL;
    ma_uint32 capCount = 0, playCount = 0;

    if (ma_context_get_devices(&s_context,
            &pPlayDevices, &playCount,
            &pCapDevices,  &capCount) == MA_SUCCESS)
    {
        if (captureIndex >= 0 && (ma_uint32)captureIndex < capCount)
            pCaptureId = &pCapDevices[captureIndex].id;
        if (playbackIndex >= 0 && (ma_uint32)playbackIndex < playCount)
            pPlaybackId = &pPlayDevices[playbackIndex].id;
    }

    /* Configure full-duplex device */
    s_config = ma_device_config_init(ma_device_type_duplex);
    s_config.capture.pDeviceID          = pCaptureId;
    s_config.capture.format             = ma_format_s16;
    s_config.capture.channels           = (ma_uint32)channels;
    s_config.capture.shareMode          = ma_share_mode_shared;
    s_config.playback.pDeviceID         = pPlaybackId;
    s_config.playback.format            = ma_format_s16;
    s_config.playback.channels          = (ma_uint32)channels;
    s_config.playback.shareMode         = ma_share_mode_shared;
    s_config.sampleRate                 = (ma_uint32)sampleRate;
    s_config.periodSizeInFrames         = (ma_uint32)bufferFrames;
    s_config.dataCallback               = data_callback;

    if (ma_device_init(&s_context, &s_config, &s_device) != MA_SUCCESS)
    {
        set_error("ma_device_init mislukt — controleer audio apparaten");
        ma_context_uninit(&s_context);
        return -2;
    }

    s_initialized = 1;
    return 0;
}

/* ── Start ───────────────────────────────────────────────────────────────── */

RB_EXPORT int rb_audio_start(void)
{
    if (!s_initialized)
    {
        set_error("rb_audio_start: niet geïnitialiseerd");
        return -1;
    }
    if (s_started) return 0;

    if (ma_device_start(&s_device) != MA_SUCCESS)
    {
        set_error("ma_device_start mislukt");
        return -2;
    }
    s_started = 1;
    return 0;
}

/* ── Stop ────────────────────────────────────────────────────────────────── */

RB_EXPORT void rb_audio_stop(void)
{
    if (!s_started) return;
    ma_device_stop(&s_device);
    s_started = 0;
}

/* ── Uninit ──────────────────────────────────────────────────────────────── */

RB_EXPORT void rb_audio_uninit(void)
{
    rb_audio_stop();
    if (s_initialized)
    {
        ma_device_uninit(&s_device);
        ma_context_uninit(&s_context);
        s_initialized = 0;
    }
    s_captureCallback  = NULL;
    s_playbackCallback = NULL;
}

/* ── Device enumeration ──────────────────────────────────────────────────── */

RB_EXPORT int rb_get_device_count(int isCapture)
{
    ma_context ctx;
    if (ma_context_init(NULL, 0, NULL, &ctx) != MA_SUCCESS) return 0;

    ma_device_info* pPlayDevices = NULL;
    ma_device_info* pCapDevices  = NULL;
    ma_uint32 playCount = 0, capCount = 0;

    int result = 0;
    if (ma_context_get_devices(&ctx, &pPlayDevices, &playCount,
                                     &pCapDevices,  &capCount) == MA_SUCCESS)
        result = (int)(isCapture ? capCount : playCount);

    ma_context_uninit(&ctx);
    return result;
}

RB_EXPORT void rb_get_device_name(int isCapture, int index, char* name, int len)
{
    if (!name || len <= 0) return;
    name[0] = '\0';

    ma_context ctx;
    if (ma_context_init(NULL, 0, NULL, &ctx) != MA_SUCCESS) return;

    ma_device_info* pPlayDevices = NULL;
    ma_device_info* pCapDevices  = NULL;
    ma_uint32 playCount = 0, capCount = 0;

    if (ma_context_get_devices(&ctx, &pPlayDevices, &playCount,
                                     &pCapDevices,  &capCount) == MA_SUCCESS)
    {
        ma_device_info* list  = isCapture ? pCapDevices  : pPlayDevices;
        ma_uint32       count = isCapture ? capCount     : playCount;

        if (index >= 0 && (ma_uint32)index < count)
            strncpy(name, list[index].name, len - 1);
    }

    name[len - 1] = '\0';
    ma_context_uninit(&ctx);
}

/* ── Last error ──────────────────────────────────────────────────────────── */

RB_EXPORT const char* rb_get_last_error(void)
{
    return s_lastError;
}
