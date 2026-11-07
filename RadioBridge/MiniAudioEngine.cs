using System;
using System.Runtime.InteropServices;

namespace RadioBridge
{
    /// <summary>
    /// P/Invoke wrapper around the native radiobridge_audio shim (miniaudio).
    /// Works on Windows (radiobridge_audio.dll) and Linux (radiobridge_audio.so).
    /// </summary>
    internal static class MiniAudioEngine
    {
        // ── Library name ───────────────────────────────────────────────────────
        // .NET resolves "radiobridge_audio" →
        //   Windows : radiobridge_audio.dll
        //   Linux   : radiobridge_audio.so  (via DllImport convention)
        private const string Lib = "radiobridge_audio";

        // ── Delegate types (must match the C typedefs) ─────────────────────────

        /// <summary>Called by native code when captured PCM samples are ready.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void CaptureCallback(IntPtr buffer, int frames);

        /// <summary>Called by native code to fill the playback buffer.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void PlaybackCallback(IntPtr buffer, int frames);

        // ── P/Invoke declarations ──────────────────────────────────────────────

        /// <summary>
        /// Initialise capture + playback.
        /// captureIndex / playbackIndex: -1 = default device.
        /// Returns 0 on success.
        /// </summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int rb_audio_init(
            int captureIndex, int playbackIndex,
            int sampleRate, int channels, int bufferFrames,
            CaptureCallback captureCallback,
            PlaybackCallback playbackCallback);

        /// <summary>Start audio streaming. Call after rb_audio_init.</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int rb_audio_start();

        /// <summary>Stop audio streaming.</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void rb_audio_stop();

        /// <summary>Free all resources.</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void rb_audio_uninit();

        /// <summary>
        /// Number of available devices.
        /// isCapture: 1 = input, 0 = output.
        /// </summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int rb_get_device_count(int isCapture);

        /// <summary>Fill <paramref name="name"/> with the device's display name.</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void rb_get_device_name(int isCapture, int index,
            System.Text.StringBuilder name, int len);

        /// <summary>Last error message from the native layer (static buffer).</summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr rb_get_last_error();

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Returns the last native error as a managed string.</summary>
        public static string GetLastError()
            => Marshal.PtrToStringAnsi(rb_get_last_error()) ?? string.Empty;

        /// <summary>
        /// Enumerate all capture or playback device names.
        /// isCapture: true = microphones, false = speakers/outputs.
        /// </summary>
        public static string[] GetDeviceNames(bool isCapture)
        {
            int flag  = isCapture ? 1 : 0;
            int count = rb_get_device_count(flag);
            var names = new string[count];
            var sb    = new System.Text.StringBuilder(256);

            for (int i = 0; i < count; i++)
            {
                sb.Clear();
                rb_get_device_name(flag, i, sb, sb.Capacity);
                names[i] = sb.ToString();
            }
            return names;
        }

        /// <summary>
        /// Copy <paramref name="frames"/> frames of 16-bit PCM from an IntPtr into a short[].
        /// </summary>
        public static short[] ReadShorts(IntPtr ptr, int frames, int channels)
        {
            int samples = frames * channels;
            var buf     = new short[samples];
            Marshal.Copy(ptr, buf, 0, samples);
            return buf;
        }

        /// <summary>
        /// Copy <paramref name="samples"/> shorts from <paramref name="src"/> into an IntPtr.
        /// Writes silence when src is null or too short.
        /// </summary>
        public static void WriteShorts(IntPtr dst, short[]? src, int samples)
        {
            if (src != null && src.Length >= samples)
                Marshal.Copy(src, 0, dst, samples);
            else
            {
                // Silence
                for (int i = 0; i < samples; i++)
                    Marshal.WriteInt16(dst + i * 2, 0);
            }
        }
    }
}
