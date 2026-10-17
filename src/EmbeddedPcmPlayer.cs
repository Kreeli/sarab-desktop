using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SarabDesktop
{
    // Plays embedded PCM directly through the Windows waveform API. No codecs,
    // Media Foundation, Windows Media Player, extraction or external process is needed.
    internal sealed class EmbeddedPcmPlayer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct WaveFormat
        {
            public ushort Format, Channels;
            public uint SamplesPerSecond, BytesPerSecond;
            public ushort BlockAlign, BitsPerSample, ExtraSize;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct WaveHeader
        {
            public IntPtr Data;
            public uint Length, Recorded;
            public UIntPtr User;
            public uint Flags, Loops;
            public IntPtr Next;
            public UIntPtr Reserved;
        }
        [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr handle);
        readonly DispatcherTimer timer;
        readonly Func<IntPtr, uint> resetDevice, closeDevice;
        readonly Func<IntPtr, IntPtr, uint, uint> unprepareHeader;
        internal bool HasPendingNativeResources => handle != IntPtr.Zero || header != IntPtr.Zero || samples != IntPtr.Zero;
        internal string CleanupError { get; private set; }
        IntPtr handle, header, samples;
        bool prepared, playing, disposed;
        public double Volume { get; set; } = 1.0;
        public bool IsMuted => Volume == 0;
        public event EventHandler MediaOpened;
        public event EventHandler MediaEnded;
        public event EventHandler<PlaybackErrorEventArgs> MediaFailed;
        internal sealed class PlaybackErrorEventArgs : EventArgs
        {
            public Exception ErrorException { get; private set; }
            public PlaybackErrorEventArgs(Exception exception) { ErrorException = exception; }
        }
        public EmbeddedPcmPlayer() : this(waveOutReset, waveOutUnprepareHeader, waveOutClose) { }
        internal EmbeddedPcmPlayer(Func<IntPtr, uint> reset, Func<IntPtr, IntPtr, uint, uint> unprepare, Func<IntPtr, uint> close)
        {
            resetDevice = reset ?? throw new ArgumentNullException(nameof(reset));
            unprepareHeader = unprepare ?? throw new ArgumentNullException(nameof(unprepare));
            closeDevice = close ?? throw new ArgumentNullException(nameof(close));
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += OnTick;
        }
        internal static byte[] ReadPcm(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek) throw new InvalidDataException("PCM input must be readable and seekable.");
            if (stream.Length < 44 || stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("PCM resource size is invalid.");
            using (var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, true))
            {
                if (new string(reader.ReadChars(4)) != "RIFF" || reader.ReadUInt32() != stream.Length - 8 || new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Invalid WAV container.");
                bool formatFound = false;
                byte[] pcm = null;
                while (stream.Position + 8 <= stream.Length)
                {
                    string chunk = new string(reader.ReadChars(4)); uint length = reader.ReadUInt32();
                    long end = stream.Position + length;
                    if (end > stream.Length) throw new InvalidDataException("Truncated WAV chunk.");
                    if (chunk == "fmt ")
                    {
                        if (formatFound || length < 16 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 1 || reader.ReadUInt32() != 11025 || reader.ReadUInt32() != 11025 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 8) throw new InvalidDataException("Expected mono 11025 Hz unsigned 8-bit PCM.");
                        formatFound = true;
                    }
                    else if (chunk == "data")
                    {
                        if (pcm != null || length == 0) throw new InvalidDataException("Invalid PCM data block.");
                        pcm = reader.ReadBytes(checked((int)length));
                    }
                    stream.Position = end;
                    if ((length & 1) != 0 && stream.Position < stream.Length) stream.Position++;
                }
                if (!formatFound || pcm == null || stream.Position != stream.Length) throw new InvalidDataException("Incomplete WAV resource.");
                return pcm;
            }
        }
        static void Check(uint result, string operation)
        {
            if (result != 0) throw new InvalidOperationException(operation + " failed (waveOut error " + result + ").");
        }
        public void Open(Stream stream)
        {
            if (disposed) throw new ObjectDisposedException(nameof(EmbeddedPcmPlayer));
            Close();
            if (HasPendingNativeResources)
            {
                MediaFailed?.Invoke(this, new PlaybackErrorEventArgs(new InvalidOperationException("Previous audio resources are still owned by the device: " + CleanupError)));
                return;
            }
            try
            {
                byte[] data = ReadPcm(stream);
                if (Volume == 0) for (int i = 0; i < data.Length; i++) data[i] = 128;
                else if (Volume != 1) throw new InvalidOperationException("Only full volume and muted preview playback are supported.");
                var format = new WaveFormat { Format = 1, Channels = 1, SamplesPerSecond = 11025, BytesPerSecond = 11025, BlockAlign = 1, BitsPerSample = 8 };
                Check(waveOutOpen(out handle, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0), "Open audio device");
                samples = Marshal.AllocHGlobal(data.Length); Marshal.Copy(data, 0, samples, data.Length);
                header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveHeader)));
                Marshal.StructureToPtr(new WaveHeader { Data = samples, Length = (uint)data.Length }, header, false);
                Check(waveOutPrepareHeader(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader))), "Prepare PCM buffer");
                prepared = true;
                MediaOpened?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Close();
                MediaFailed?.Invoke(this, new PlaybackErrorEventArgs(ex));
            }
        }
        public void Play()
        {
            if (handle == IntPtr.Zero || !prepared || playing) return;
            try
            {
                Check(waveOutWrite(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader))), "Play PCM buffer");
                playing = true; timer.Start();
            }
            catch (Exception ex)
            {
                Close();
                MediaFailed?.Invoke(this, new PlaybackErrorEventArgs(ex));
            }
        }
        void OnTick(object sender, EventArgs args)
        {
            if (!playing || header == IntPtr.Zero) return;
            if ((((WaveHeader)Marshal.PtrToStructure(header, typeof(WaveHeader))).Flags & 1) == 0) return;
            timer.Stop(); playing = false;
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }
        public void Stop() { Close(); }
        public void Close()
        {
            timer.Stop(); playing = false;
            CleanupError = null;
            if (handle != IntPtr.Zero)
            {
                // A failed reset/unprepare can mean that the driver still holds these
                // pointers. Keep the handle and allocations intact instead of risking
                // native use-after-free. A later explicit Close can retry; there is no
                // busy retry loop, and Open cannot overwrite an unreleased handle.
                uint result = resetDevice(handle);
                if (result != 0) { CleanupError = "waveOutReset: " + result; return; }
                if (prepared && header != IntPtr.Zero)
                {
                    result = unprepareHeader(handle, header, (uint)Marshal.SizeOf(typeof(WaveHeader)));
                    if (result != 0) { CleanupError = "waveOutUnprepareHeader: " + result; return; }
                    prepared = false;
                }
                result = closeDevice(handle);
                if (result != 0) { CleanupError = "waveOutClose: " + result; return; }
                handle = IntPtr.Zero;
            }
            prepared = false;
            if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
            if (samples != IntPtr.Zero) { Marshal.FreeHGlobal(samples); samples = IntPtr.Zero; }
        }
        public void Dispose()
        {
            Close();
            if (!disposed) { disposed = true; timer.Tick -= OnTick; }
        }
    }
}
