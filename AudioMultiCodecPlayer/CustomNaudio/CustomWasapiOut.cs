
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NAudio.Wave;

namespace AudioMultiCodecPlayer.CustomNaudio
{
    //
    // 概要:
    //     Support for playback using Wasapi
    public class CustomWasapiOut : IWavePlayer, IDisposable, IWavePosition
    {
        public AudioClient audioClient;

        private readonly MMDevice mmDevice;

        private readonly AudioClientShareMode shareMode;

        private AudioRenderClient renderClient;

        private IWaveProvider sourceProvider;

        private int latencyMilliseconds;

        private int bufferFrameCount;

        private int bytesPerFrame;

        private readonly bool isUsingEventSync;

        private EventWaitHandle frameEventWaitHandle;

        private byte[] readBuffer;

        private volatile PlaybackState playbackState;

        private Thread playThread;

        private readonly SynchronizationContext syncContext;

        private bool dmoResamplerNeeded;

        //
        // 概要:
        //     Gets a NAudio.Wave.WaveFormat instance indicating the format the hardware is
        //     using.
        public WaveFormat OutputWaveFormat { get; private set; }

        //
        // 概要:
        //     Playback State
        public PlaybackState PlaybackState => playbackState;

        //
        // 概要:
        //     Volume
        public float Volume
        {
            get
            {
                return mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            set
            {
                if (value < 0f)
                {
                    throw new ArgumentOutOfRangeException("value", "Volume must be between 0.0 and 1.0");
                }

                if (value > 1f)
                {
                    throw new ArgumentOutOfRangeException("value", "Volume must be between 0.0 and 1.0");
                }

                mmDevice.AudioEndpointVolume.MasterVolumeLevelScalar = value;
            }
        }

        //
        // 概要:
        //     Retrieve the AudioStreamVolume object for this audio stream
        //
        // 例外:
        //   T:System.InvalidOperationException:
        //     This is thrown when an exclusive audio stream is being used.
        //
        // 注釈:
        //     This returns the AudioStreamVolume object ONLY for shared audio streams.
        public AudioStreamVolume AudioStreamVolume
        {
            get
            {
                if (shareMode == AudioClientShareMode.Exclusive)
                {
                    throw new InvalidOperationException("AudioStreamVolume is ONLY supported for shared audio streams.");
                }

                return audioClient.AudioStreamVolume;
            }
        }

        //
        // 概要:
        //     Playback Stopped
        public event EventHandler<StoppedEventArgs> PlaybackStopped;

        //
        // 概要:
        //     WASAPI Out shared mode, default
        public CustomWasapiOut()
            : this(GetDefaultAudioEndpoint(), AudioClientShareMode.Shared, useEventSync: true, 200)
        {
        }

        //
        // 概要:
        //     WASAPI Out using default audio endpoint
        //
        // パラメーター:
        //   shareMode:
        //     ShareMode - shared or exclusive
        //
        //   latency:
        //     Desired latency in milliseconds
        public CustomWasapiOut(AudioClientShareMode shareMode, int latency)
            : this(GetDefaultAudioEndpoint(), shareMode, useEventSync: true, latency)
        {
        }

        //
        // 概要:
        //     WASAPI Out using default audio endpoint
        //
        // パラメーター:
        //   shareMode:
        //     ShareMode - shared or exclusive
        //
        //   useEventSync:
        //     true if sync is done with event. false use sleep.
        //
        //   latency:
        //     Desired latency in milliseconds
        public CustomWasapiOut(AudioClientShareMode shareMode, bool useEventSync, int latency)
            : this(GetDefaultAudioEndpoint(), shareMode, useEventSync, latency)
        {
        }

        //
        // 概要:
        //     Creates a new WASAPI Output
        //
        // パラメーター:
        //   device:
        //     Device to use
        //
        //   shareMode:
        //
        //   useEventSync:
        //     true if sync is done with event. false use sleep.
        //
        //   latency:
        //     Desired latency in milliseconds
        public CustomWasapiOut(MMDevice device, AudioClientShareMode shareMode, bool useEventSync, int latency)
        {
            audioClient = device.AudioClient;
            mmDevice = device;
            this.shareMode = shareMode;
            isUsingEventSync = useEventSync;
            latencyMilliseconds = latency;
            syncContext = SynchronizationContext.Current;
            OutputWaveFormat = audioClient.MixFormat;
        }

        private static MMDevice GetDefaultAudioEndpoint()
        {
            if (Environment.OSVersion.Version.Major < 6)
            {
                throw new NotSupportedException("WASAPI supported only on Windows Vista and above");
            }

            return new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        }

        private void PlayThread()
        {
            ResamplerDmoStream resamplerDmoStream = null;
            IWaveProvider playbackProvider = sourceProvider;
            Exception e = null;
            try
            {
                if (dmoResamplerNeeded)
                {
                    resamplerDmoStream = new ResamplerDmoStream(sourceProvider, OutputWaveFormat);
                    playbackProvider = resamplerDmoStream;
                }

                bufferFrameCount = audioClient.BufferSize;
                bytesPerFrame = OutputWaveFormat.Channels * OutputWaveFormat.BitsPerSample / 8;
                readBuffer = BufferHelpers.Ensure(readBuffer, bufferFrameCount * bytesPerFrame);
                if (FillBuffer(playbackProvider, bufferFrameCount))
                {
                    return;
                }

                WaitHandle[] waitHandles = new WaitHandle[1] { frameEventWaitHandle };
                audioClient.Start();
                while (playbackState != 0)
                {
                    if (isUsingEventSync)
                    {
                        WaitHandle.WaitAny(waitHandles, 3 * latencyMilliseconds, exitContext: false);
                    }
                    else
                    {
                        Thread.Sleep(latencyMilliseconds / 2);
                    }

                    if (playbackState == PlaybackState.Playing)
                    {
                        int num = ((!isUsingEventSync) ? audioClient.CurrentPadding : ((shareMode == AudioClientShareMode.Shared) ? audioClient.CurrentPadding : 0));
                        int num2 = bufferFrameCount - num;
                        if (num2 > 10 && FillBuffer(playbackProvider, num2))
                        {
                            break;
                        }
                    }
                }

                if (playbackState == PlaybackState.Playing)
                {
                    Thread.Sleep(isUsingEventSync ? latencyMilliseconds : (latencyMilliseconds / 2));
                }

                audioClient.Stop();
                playbackState = PlaybackState.Stopped;
                audioClient.Reset();
            }
            catch (Exception ex)
            {
                e = ex;
            }
            finally
            {
                resamplerDmoStream?.Dispose();
                RaisePlaybackStopped(e);
            }
        }

        private void RaisePlaybackStopped(Exception e)
        {
            EventHandler<StoppedEventArgs> handler = this.PlaybackStopped;
            if (handler == null)
            {
                return;
            }

            if (syncContext == null)
            {
                handler(this, new StoppedEventArgs(e));
                return;
            }

            syncContext.Post(delegate
            {
                handler(this, new StoppedEventArgs(e));
            }, null);
        }

        //
        // 概要:
        //     returns true if reached the end
        private unsafe bool FillBuffer(IWaveProvider playbackProvider, int frameCount)
        {
            int num = frameCount * bytesPerFrame;
            int num2 = playbackProvider.Read(readBuffer, 0, num);
            if (num2 == 0)
            {
                return true;
            }

            IntPtr buffer = renderClient.GetBuffer(frameCount);
            Marshal.Copy(readBuffer, 0, buffer, num2);
            if (isUsingEventSync && shareMode == AudioClientShareMode.Exclusive)
            {
                if (num2 < num)
                {
                    byte* ptr = (byte*)(void*)buffer;
                    while (num2 < num)
                    {
                        ptr[num2++] = 0;
                    }
                }

                renderClient.ReleaseBuffer(frameCount, AudioClientBufferFlags.None);
            }
            else
            {
                int numFramesWritten = num2 / bytesPerFrame;
                renderClient.ReleaseBuffer(numFramesWritten, AudioClientBufferFlags.None);
            }

            return false;
        }

        private WaveFormat GetFallbackFormat()
        {
            int sampleRate = audioClient.MixFormat.SampleRate;
            int channels = audioClient.MixFormat.Channels;
            List<int> list = new List<int> { OutputWaveFormat.SampleRate };
            if (!list.Contains(sampleRate))
            {
                list.Add(sampleRate);
            }

            if (!list.Contains(44100))
            {
                list.Add(44100);
            }

            if (!list.Contains(48000))
            {
                list.Add(48000);
            }

            List<int> list2 = new List<int> { OutputWaveFormat.Channels };
            if (!list2.Contains(channels))
            {
                list2.Add(channels);
            }

            if (!list2.Contains(2))
            {
                list2.Add(2);
            }

            List<int> list3 = new List<int> { OutputWaveFormat.BitsPerSample };
            if (!list3.Contains(32))
            {
                list3.Add(32);
            }

            if (!list3.Contains(24))
            {
                list3.Add(24);
            }

            if (!list3.Contains(16))
            {
                list3.Add(16);
            }

            foreach (int item in list)
            {
                foreach (int item2 in list2)
                {
                    foreach (int item3 in list3)
                    {
                        WaveFormatExtensible waveFormatExtensible = new WaveFormatExtensible(item, item3, item2);
                        if (audioClient.IsFormatSupported(shareMode, waveFormatExtensible))
                        {
                            return waveFormatExtensible;
                        }
                    }
                }
            }

            throw new NotSupportedException("Can't find a supported format to use");
        }

        //
        // 概要:
        //     Gets the current position in bytes from the wave output device. (n.b. this is
        //     not the same thing as the position within your reader stream)
        //
        // 戻り値:
        //     Position in bytes
        public long GetPosition()
        {
            ulong position;
            switch (playbackState)
            {
                case PlaybackState.Stopped:
                    return 0L;
                case PlaybackState.Playing:
                    position = audioClient.AudioClockClient.AdjustedPosition;
                    break;
                default:
                    {
                        audioClient.AudioClockClient.GetPosition(out position, out var _);
                        break;
                    }
            }

            return (long)position * (long)OutputWaveFormat.AverageBytesPerSecond / (long)audioClient.AudioClockClient.Frequency;
        }

        //
        // 概要:
        //     Begin Playback
        public void Play()
        {
            if (playbackState != PlaybackState.Playing)
            {
                if (playbackState == PlaybackState.Stopped)
                {
                    playThread = new Thread(PlayThread)
                    {
                        IsBackground = true
                    };
                    playbackState = PlaybackState.Playing;
                    playThread.Start();
                }
                else
                {
                    playbackState = PlaybackState.Playing;
                }
            }
        }

        //
        // 概要:
        //     Stop playback and flush buffers
        public void Stop()
        {
            if (playbackState != 0)
            {
                playbackState = PlaybackState.Stopped;
                playThread.Join();
                playThread = null;
            }
        }

        //
        // 概要:
        //     Stop playback without flushing buffers
        public void Pause()
        {
            if (playbackState == PlaybackState.Playing)
            {
                playbackState = PlaybackState.Paused;
            }
        }

        //
        // 概要:
        //     Initialize for playing the specified wave stream
        //
        // パラメーター:
        //   waveProvider:
        //     IWaveProvider to play
        public void Init(IWaveProvider waveProvider)
        {
            long num = (long)latencyMilliseconds * 10000L;
            OutputWaveFormat = waveProvider.WaveFormat;
            AudioClientStreamFlags audioClientStreamFlags = AudioClientStreamFlags.SrcDefaultQuality | AudioClientStreamFlags.AutoConvertPcm;
            sourceProvider = waveProvider;
            if (shareMode == AudioClientShareMode.Exclusive)
            {
                audioClientStreamFlags = AudioClientStreamFlags.None;
                if (!audioClient.IsFormatSupported(shareMode, OutputWaveFormat, out var closestMatchFormat))
                {
                    if (closestMatchFormat == null)
                    {
                        OutputWaveFormat = GetFallbackFormat();
                    }
                    else
                    {
                        OutputWaveFormat = closestMatchFormat;
                    }

                    try
                    {
                        using (new ResamplerDmoStream(waveProvider, OutputWaveFormat))
                        {
                        }
                    }
                    catch (Exception)
                    {
                        OutputWaveFormat = GetFallbackFormat();
                        using (new ResamplerDmoStream(waveProvider, OutputWaveFormat))
                        {
                        }
                    }

                    dmoResamplerNeeded = true;
                }
                else
                {
                    dmoResamplerNeeded = false;
                }
            }

            if (isUsingEventSync)
            {
                if (shareMode == AudioClientShareMode.Shared)
                {
                    audioClient.Initialize(shareMode, AudioClientStreamFlags.EventCallback | audioClientStreamFlags, num, 0L, OutputWaveFormat, Guid.Empty);
                    long streamLatency = audioClient.StreamLatency;
                    if (streamLatency != 0L)
                    {
                        latencyMilliseconds = (int)(streamLatency / 10000);
                    }
                }
                else
                {
                    try
                    {
                        audioClient.Initialize(shareMode, AudioClientStreamFlags.EventCallback | audioClientStreamFlags, num, num, OutputWaveFormat, Guid.Empty);
                    }
                    catch (COMException ex2)
                    {
                        if (ex2.ErrorCode != -2004287463)
                        {
                            throw;
                        }

                        long num2 = (long)(10000000.0 / (double)OutputWaveFormat.SampleRate * (double)audioClient.BufferSize + 0.5);
                        audioClient.Dispose();
                        audioClient = mmDevice.AudioClient;
                        audioClient.Initialize(shareMode, AudioClientStreamFlags.EventCallback | audioClientStreamFlags, num2, num2, OutputWaveFormat, Guid.Empty);
                    }
                }

                frameEventWaitHandle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset);
                audioClient.SetEventHandle(frameEventWaitHandle.SafeWaitHandle.DangerousGetHandle());
            }
            else
            {
                audioClient.Initialize(shareMode, audioClientStreamFlags, num, 0L, OutputWaveFormat, Guid.Empty);
            }

            renderClient = audioClient.AudioRenderClient;
        }

        //
        // 概要:
        //     Dispose
        public void Dispose()
        {
            if (audioClient != null)
            {
                Stop();
                audioClient.Dispose();
                audioClient = null;
                renderClient = null;
            }
        }
    }
}
