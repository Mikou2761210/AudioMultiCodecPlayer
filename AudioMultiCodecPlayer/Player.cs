using AudioMultiCodecPlayer.CustomNaudio;
using AudioMultiCodecPlayer.CustomWaveProvider;
using AudioMultiCodecPlayer.Helper;
using MikouTools.UtilityTools.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace AudioMultiCodecPlayer
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
        public enum PlayerState
        {
            Uninitialized,
            None,
            Dispose
        }
    }

    public partial class Player : IDisposable
    {
        MikouTools.ThreadTools.ThreadManager _threadManager = new MikouTools.ThreadTools.ThreadManager("Player",true, ApartmentState.MTA);

        internal CustomNaudio.CustomWasapiOut? wasapiOut;

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
                _threadManager.Invoke(() => 
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

        public Action<double>? VolumeChange { get { StateCheckHelper(); return _mMDeviceHelper.VolumeChange; } set { StateCheckHelper(); _mMDeviceHelper.VolumeChange = value; } }
        public Action<bool>? MuteChange { get { StateCheckHelper(); return _mMDeviceHelper.MuteChange; } set { StateCheckHelper(); _mMDeviceHelper.MuteChange = value; } }

        CustomBaseProvider? Provider;

        MMDeviceHelper _mMDeviceHelper;

        MikouTools.ThreadTools.ThreadResourceManager _threadResourceManager = new MikouTools.ThreadTools.ThreadResourceManager();

        public Player()
        {
            _state.Lock();
            try
            {


                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                //Initialize
                _mMDeviceHelper = new MMDeviceHelper(_threadManager);

                _threadResourceManager.CleanupTaskAdd(_threadManager, _threadManager.Dispose);

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
            _threadManager.Invoke(() => 
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

                wasapiOut = new CustomNaudio.CustomWasapiOut(_mMDeviceHelper.MMDevice, AudioClientShareMode.Shared, true, 200);
                wasapiOut.Init(Provider);
                wasapiOut.PlaybackStopped += (s,e) => { if (_state.Value != PlayerState.Dispose && CurrentSeconds >= Provider.TotleTime.TotalSeconds) AudioEnd?.Invoke(); AudioStop?.Invoke(); };
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

            });
            AudioOpen?.Invoke();
        }

        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Skip(double seconds) => CurrentSeconds += seconds;
        public double CurrentSeconds
        {
            get
            {
                if (_state.Value != PlayerState.None || Provider == null) return 0;
                double currentpadding = 0;
                _threadManager.Invoke(() => { if (wasapiOut != null) currentpadding = wasapiOut.audioClient.CurrentPadding; });
                //Debug.WriteLine($"{Provider.CurrentTime.TotalSeconds} - ({currentpadding} / {Provider.WaveFormat.SampleRate})({currentpadding / Provider.WaveFormat.SampleRate})");
                return Provider.CurrentTime.TotalSeconds - (currentpadding / (double)Provider.WaveFormat.SampleRate);
            }
            set
            {
                if (_state.Value != PlayerState.None || Provider == null) return;
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
                if (_state.Value != PlayerState.None || Provider == null) return;
                Provider.CurrentTime = value;
            }
        }

        public TimeSpan TotleTime
        {
            get
            {
                if (_state.Value != PlayerState.None || Provider == null) return TimeSpan.Zero;
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

        public PlayerState State => _state.Value;
        public void Dispose()
        {
            if (_state.SetAndReturnOld(PlayerState.Dispose) != PlayerState.Dispose)
            {
                _threadManager.Dispose();
                _mMDeviceHelper.Dispose();
                wasapiOut?.Dispose();
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            }

        }
    }

}
