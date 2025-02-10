using MikouTools;
using MikouTools.ThreadTools;
using MikouTools.UtilityTools.Threading;
using MultiCodecPlayer.CustomWaveProvider;
using MultiCodecPlayer.Helper;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MultiCodecPlayer
{
    public partial class Player : IDisposable
    {
        public enum PlayerAudioDeviceMode
        {
            Auto, Manual
        }
        public enum PlayerMode
        {
            AudioFileReader, Opus
        }
        internal enum PlayerState
        {
            Uninitialized,
            None,
            Dispose
        }
    }

    public partial class Player : IDisposable
    {
        MikouTools.ThreadTools.ThreadManager threadManager = new MikouTools.ThreadTools.ThreadManager("Player");

        internal WasapiOut? wasapiOut;

        public Action? AudioOpen;
        public Action? AudioEnd;
        public Action? AudioStop;
        public Action<string>? Error;

        public Action? PlaybackStateChange;
        PlaybackState playbackState_;
        readonly object playbackStateLock = new object();
        public PlaybackState PlaybackState
        {
            get
            {
                StateCheckHelper();
                lock (playbackStateLock)
                    return playbackState_;
            }
            set
            {
                StateCheckHelper();
                threadManager.Invoke(() => 
                { 
                    lock (playbackStateLock) 
                    {
                        playbackState_ = value;
                        switch (value)
                        {
                            case PlaybackState.Playing:
                                wasapiOut?.Play();
                                break;
                            case PlaybackState.Paused:
                                wasapiOut?.Pause();
                                break;
                            case PlaybackState.Stopped:
                                wasapiOut?.Stop();
                                Provider?.Dispose();
                                Provider = null;
                                break;
                        }
                    } 
                });
            }
        }

        //System.Threading.Timer VolumeTimer = null!;
        public Action<double>? VolumeChange { get { StateCheckHelper(); return _mMDeviceHelper.VolumeChange; } set { StateCheckHelper(); _mMDeviceHelper.VolumeChange = value; } }
        public Action<bool>? MuteChange { get { StateCheckHelper(); return _mMDeviceHelper.MuteChange; } set { StateCheckHelper(); _mMDeviceHelper.MuteChange = value; } }

        CustomBaseProvider? Provider;

        MMDeviceHelper _mMDeviceHelper;

        MikouTools.ThreadTools.ThreadResourceManager threadResourceManager = new MikouTools.ThreadTools.ThreadResourceManager();

        public Player()
        {
            _state.Lock();
            try
            {


                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                //Initialize
                _mMDeviceHelper = new MMDeviceHelper(threadManager);

                threadResourceManager.CleanupTaskAdd(threadManager, threadManager.Dispose);
            }
            finally 
            { 
                _state.AccessValueWhileLocked = PlayerState.None;
                _state.UnLock();
            }
        }


        public double? Volume
        {
            get { StateCheckHelper(); return _mMDeviceHelper.Volume; }
            set { StateCheckHelper(); if (value != null) _mMDeviceHelper.Volume = (float)value; }
        }

        public bool? Mute
        {
            get { StateCheckHelper(); return _mMDeviceHelper.Mute; }
            set { StateCheckHelper(); if (value != null) _mMDeviceHelper.Mute = (bool)value; }
        }



        public PlayerAudioDeviceMode AudioDeviceMode
        {
            get { StateCheckHelper(); return _mMDeviceHelper.AudioDeviceMode; }
            set { StateCheckHelper(); _mMDeviceHelper.AudioDeviceMode = value; }
        }







        // PlayerMode CurrentPlayerMode;
        public void NewPlaying(string filePath, PlayerMode? playerMode, PlaybackState playbackState = PlaybackState.Playing, TimeSpan? position = null)
        {
            StateCheckHelper();
            threadManager.Invoke(() => 
            {
                playbackState_ = PlaybackState.Stopped;

                wasapiOut?.Stop(); wasapiOut?.Dispose();

                Provider?.Dispose();
                Provider = null;
                if (playbackState == PlaybackState.Stopped) return;
                switch (playerMode)
                {
                    case PlayerMode.AudioFileReader:
                        Provider = new CustomAudioFileReader(filePath);
                        break;
                    case PlayerMode.Opus:
                        Provider = new OpusProvider(filePath);
                        break;

                    default: return;
                }

                if (position != null)
                    Provider.CurrentTime = (TimeSpan)position;

                wasapiOut = new WasapiOut(_mMDeviceHelper.MMDevice, AudioClientShareMode.Shared, true, 200);
                wasapiOut.Init(Provider);
                wasapiOut.PlaybackStopped += (s,e) => { if (CurrentSeconds >= Provider.TotleTime.TotalSeconds) AudioEnd?.Invoke(); AudioStop?.Invoke(); };
                playbackState_ = playbackState;
                switch (playbackState)
                {
                    case PlaybackState.Playing:
                        wasapiOut.Play();
                        break;
                    case PlaybackState.Paused:
                        wasapiOut.Pause();
                        break;
                }
                AudioOpen?.Invoke();
            });
        }

        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Skip(double seconds) => CurrentSeconds += seconds;
        public double CurrentSeconds
        {
            get
            {
                StateCheckHelper();
                if (Provider == null || wasapiOut == null || wasapiOut.PlaybackState == PlaybackState.Stopped) return 0;
                /*double CurrentSeconds = mmdevice.Value.AudioClient.CurrentPadding / 48000;
                mmdevice.Value.AudioClient.AudioClockClient.GetPosition(out ulong position, out _);
                Debug.WriteLine($"appo  :  {CurrentSeconds}");
                return Provider.CurrentTime.TotalSeconds - CurrentSeconds;*/
                _mMDeviceHelper.AudioClient.AudioClockClient.GetPosition(out ulong position, out _);
                return position / 48000;
            }
            set
            {
                StateCheckHelper();
                if (Provider != null)
                    Provider.CurrentTime = TimeSpan.FromSeconds(value);
            }
        }


        public TimeSpan CurrentTime
        {
            get
            {
                StateCheckHelper();
                return TimeSpan.FromSeconds(CurrentSeconds);
            }
            set
            {
                StateCheckHelper();
                if (Provider != null)
                    CurrentSeconds = value.TotalSeconds;
            }
        }

        public TimeSpan TotleTime
        {
            get
            {
                StateCheckHelper();
                if (Provider == null) return TimeSpan.Zero;
                return Provider.TotleTime;
            }
        }



        private void OnProcessExit(object? sender, EventArgs e)
        {
            Dispose();
        }

        private void StateCheckHelper()
        {
            switch (_state.Value)
            {
                case PlayerState.None:
                    break;
                case PlayerState.Dispose:
                    throw new ObjectDisposedException("Player");
                case PlayerState.Uninitialized:
                    throw new Exception("PlayerState.Uninitialized");
            }
        }

        LockableProperty<PlayerState> _state = new LockableProperty<PlayerState>(PlayerState.Uninitialized);
        public void Dispose()
        {
            if (_state.SetAndReturnOld(PlayerState.Dispose) != PlayerState.Dispose)
            {
                threadManager.Dispose();
                _mMDeviceHelper.Dispose();
                wasapiOut?.Dispose();
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            }

        }
    }


    /*    public class Player : IDisposable
    {
        //Utility.AsyncActionQueue actionQueue = new Utility.AsyncActionQueue();

        MikouTools.ThreadTools.ThreadManager threadManager = new MikouTools.ThreadTools.ThreadManager("Player");

        MMDeviceEnumerator deviceEnumerator;
        AudioSessionEventsHandler audioSessionEventsHandler;
        internal DeviceNotificationClient deviceNotificationClient;
        internal LockableProperty<MMDevice> mmdevice;
        internal LockableProperty<AudioClient?> audioClient = new LockableProperty<AudioClient?>(null);
        internal WasapiOut? wasapiOut;

        public Action? AudioOpen;
        public Action? AudioEnd;
        public Action? AudioStop;
        public Action<string>? Error;

        public Action? PlaybackStateChange;
        PlaybackState playbackState_;
        readonly object playbackStateLock = new object();
        public PlaybackState PlaybackState
        {
            get 
            {
                lock (playbackStateLock)
                    return playbackState_;
            }
            set 
            {
                threadManager.Invoke(() => 
                { 
                    lock (playbackStateLock) 
                    {
                        playbackState_ = value;
                        switch (value)
                        {
                            case PlaybackState.Playing:
                                wasapiOut?.Play();
                                break;
                            case PlaybackState.Paused:
                                wasapiOut?.Pause();
                                break;
                            case PlaybackState.Stopped:
                                wasapiOut?.Stop();
                                Provider?.Dispose();
                                Provider = null;
                                break;
                        }
                    } 
                });
            }
        }

        //System.Threading.Timer VolumeTimer = null!;
        public event Action<double>? VolumeChange = null;
        public event Action<bool>? MuteChange = null;

        CustomBaseProvider? Provider;

        readonly object _initializeLock = new object();

        public Player()
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;


            //Initialize
            lock(_initializeLock)
            {
                threadManager.Invoke(() =>
                {
                    deviceEnumerator = new MMDeviceEnumerator();
                    audioSessionEventsHandler = new AudioSessionEventsHandler();
                    deviceNotificationClient = new DeviceNotificationClient();
                    deviceEnumerator!.RegisterEndpointNotificationCallback(deviceNotificationClient);
                });


                if (deviceEnumerator == null) throw new NullReferenceException(nameof(deviceEnumerator));
                if (deviceNotificationClient == null) throw new NullReferenceException(nameof(deviceNotificationClient));
                if (audioSessionEventsHandler == null) throw new NullReferenceException(nameof(audioSessionEventsHandler));

                deviceNotificationClient.DeviceChanged += (dataflow, role, id) =>
                {
                    if ((DataFlow)dataflow == DataFlow.Render && (Role)role == Role.Multimedia)
                    {
                        DeviceChanged(id);
                    }
                };

                threadManager.Invoke(() =>
                {
                    MMDevice? newDevice = null;
                    try
                    {
                        newDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                    catch { newDevice?.Dispose(); newDevice = null; throw; }

                    mmdevice = new LockableProperty<MMDevice>(newDevice);
                    mmdevice.Lock();
                    try
                    {
                        mmdevice.AccessValueWhileLocked.AudioSessionManager.AudioSessionControl.RegisterEventClient(audioSessionEventsHandler);
                        wasapiOut = new WasapiOut(mmdevice.AccessValueWhileLocked, AudioClientShareMode.Shared, true, 200);
                    }
                    finally
                    {
                        mmdevice.UnLock();
                    }
                });


                if (mmdevice == null) throw new NullReferenceException();


                double? lastvolume = Volume;
                bool? lastmute = Mute;

                audioSessionEventsHandler.VolumeChanged += (v, m) => { if (lastvolume != v) { lastvolume = v; VolumeChange?.Invoke(v); } if (lastmute != m) { lastmute = m; MuteChange?.Invoke(m); } };

            }
        }


        public double? Volume
        {
            get { return mmdevice.Value.AudioSessionManager.SimpleAudioVolume.Volume; }
            set { if (value != null && mmdevice != null) mmdevice.Value.AudioSessionManager.SimpleAudioVolume.Volume = (float)value; }
        }

        public bool? Mute
        {
            get { return mmdevice.Value.AudioSessionManager.SimpleAudioVolume.Mute; }
            set { if (value != null && mmdevice != null) mmdevice.Value.AudioSessionManager.SimpleAudioVolume.Mute = (bool)value; }
        }






        AudioDeviceMode audioDeviceMode_;
        readonly object audioDeviceModeLock = new object();
        public AudioDeviceMode audioDeviceMode
        {
            get {lock(audioDeviceModeLock) return audioDeviceMode_; }
            set 
            {
                lock(audioDeviceModeLock)
                {
                    audioDeviceMode_ = value;
                    switch (value)
                    {
                        case AudioDeviceMode.Auto:
                            deviceNotificationClient.DeviceChanged += (dataflow, role, id) =>
                            {
                                if ((DataFlow)dataflow == DataFlow.Render && (Role)role == Role.Multimedia)
                                {
                                    DeviceChanged(id);
                                }
                            };
                            DeviceDefaultChanged();
                            break;
                        case AudioDeviceMode.Manual:
                            deviceNotificationClient.DeviceChanged = null;
                            break;
                    }
                }
            }
        }
        public void DeviceChanged(string mmdeviceid)
        {
            threadManager.Invoke(() => {
                if (mmdevice.Value.ID != mmdeviceid)
                {
                    MMDevice? newDevice = null;
                    try
                    {
                        newDevice = deviceEnumerator.GetDevice(mmdeviceid);
                    }
                    catch { newDevice?.Dispose(); newDevice = null; }
                    DeviceChanged_(newDevice);
                }
            });

        }

        internal void DeviceDefaultChanged()
        {
            threadManager.Invoke(() => {
                MMDevice? newDevice = null;
                try
                {
                    newDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                catch { newDevice?.Dispose(); newDevice = null; }
                DeviceChanged_(newDevice);
            });

        }

        internal void DeviceChanged_(MMDevice? mMDevice)
        {
            if (mMDevice != null)
            {
                if (wasapiOut != null)
                {
                    wasapiOut.Stop();
                    wasapiOut.Dispose();
                }

                mmdevice.Lock();
                try
                {
                    mmdevice.AccessValueWhileLocked.AudioSessionManager.AudioSessionControl.UnRegisterEventClient(audioSessionEventsHandler);
                    mmdevice.AccessValueWhileLocked.Dispose();

                    mmdevice.AccessValueWhileLocked = mMDevice;

                    audioClient.Value = mMDevice.AudioClient;

                    mmdevice.AccessValueWhileLocked.AudioSessionManager.AudioSessionControl.RegisterEventClient(audioSessionEventsHandler);
                    wasapiOut = new WasapiOut(mmdevice.AccessValueWhileLocked, AudioClientShareMode.Shared, true, 200);
                }
                finally
                {
                    mmdevice.UnLock();
                }

                switch (PlaybackState)
                {
                    case PlaybackState.Playing:
                        wasapiOut.Init(Provider);
                        wasapiOut.Play();
                        break;
                    case PlaybackState.Paused:
                        wasapiOut.Init(Provider);
                        wasapiOut.Pause();
                        break;
                }
            }
        }







        // PlayerMode CurrentPlayerMode;
        public void NewPlaying(string filePath, PlayerMode? playerMode, PlaybackState playbackState = PlaybackState.Playing, TimeSpan? position = null)
        {
            threadManager.Invoke(() => 
            {
                playbackState_ = PlaybackState.Stopped;

                wasapiOut?.Stop(); wasapiOut?.Dispose();

                Provider?.Dispose();
                Provider = null;
                if (playbackState == PlaybackState.Stopped) return;
                switch (playerMode)
                {
                    case PlayerMode.AudioFileReader:
                        Provider = new CustomAudioFileReader(filePath);
                        break;
                    case PlayerMode.Opus:
                        Provider = new OpusProvider(filePath);
                        break;

                    default: return;
                }

                if (position != null)
                    Provider.CurrentTime = (TimeSpan)position;

                wasapiOut = new WasapiOut(mmdevice.Value, AudioClientShareMode.Shared, true, 200);
                wasapiOut.Init(Provider);
                wasapiOut.PlaybackStopped += (s,e) => { if (CurrentSeconds >= Provider.TotleTime.TotalSeconds) AudioEnd?.Invoke(); AudioStop?.Invoke(); };
                playbackState_ = playbackState;
                switch (playbackState)
                {
                    case PlaybackState.Playing:
                        wasapiOut.Play();
                        break;
                    case PlaybackState.Paused:
                        wasapiOut.Pause();
                        break;
                }
                AudioOpen?.Invoke();
            });
        }

        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Skip(double seconds) => CurrentSeconds += seconds;
        public double CurrentSeconds
        {
            get
            {
                if(Provider == null || mmdevice.Value.State != DeviceState.Active || dispose.Value || wasapiOut == null || wasapiOut.PlaybackState == PlaybackState.Stopped || audioClient == null) return 0;
                double CurrentSeconds = mmdevice.Value.AudioClient.CurrentPadding / 48000;
                mmdevice.Value.AudioClient.AudioClockClient.GetPosition(out ulong position, out _);
                Debug.WriteLine($"appo  :  {CurrentSeconds}");
                return Provider.CurrentTime.TotalSeconds - CurrentSeconds;
            }
            set
            {
                if (Provider != null)
                    Provider.CurrentTime = TimeSpan.FromSeconds(value);
            }
        }


        public TimeSpan CurrentTime
        {
            get
            {
                return TimeSpan.FromSeconds(CurrentSeconds);
            }
            set
            {
                if (Provider != null)
                    CurrentSeconds = value.TotalSeconds;
            }
        }

        public TimeSpan TotleTime
        {
            get
            {
                if(Provider == null) return TimeSpan.Zero;
                return Provider.TotleTime;
            }
        }



        private void OnProcessExit(object? sender, EventArgs e)
        {
            Dispose();
        }

        LockableProperty<bool> dispose = new LockableProperty<bool>(false);
        public void Dispose()
        {
            if (!dispose.SetAndReturnOld(true))
            {
                threadManager.Dispose();
                deviceNotificationClient.DeviceChanged = null;
                deviceEnumerator.UnregisterEndpointNotificationCallback(deviceNotificationClient);
                deviceEnumerator.Dispose();
                mmdevice?.Value.AudioSessionManager.AudioSessionControl.UnRegisterEventClient(audioSessionEventsHandler);
                mmdevice?.Value.Dispose();
                wasapiOut?.Dispose();
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            }

        }
    }*/
}
