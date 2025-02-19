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
        PlaybackState _playbackState;
        readonly object playbackStateLock = new object();
        public PlaybackState PlaybackState
        {
            get
            {
                StateCheckHelper();
                lock (playbackStateLock)
                    return _playbackState;
            }
            set
            {
                StateCheckHelper();
                _threadManager.Invoke(() => 
                { 
                    lock (playbackStateLock) 
                    {
                        _playbackState = value;
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
                _mMDeviceHelper.MMDeviceChangeStart += () => {
                    _state.Lock();
                    if (_playbackState != PlaybackState.Stopped)
                    {
                        try
                        {
                            if(wasapiOut != null)
                            {
                                wasapiOut.PlaybackStopped -= PlaybackStopped;
                                wasapiOut.Stop();
                                wasapiOut.Dispose();
                            }
                        }
                        catch(Exception ex)
                        {
                            _state.UnLock();
                            throw ex;
                        }
                    }
                    else
                    {
                        _state.UnLock();
                    }
                };
                _mMDeviceHelper.MMDeviceChangeEnd += () => {
                    if (_playbackState != PlaybackState.Stopped)
                    {
                        try
                        {
                            if (Provider == null) throw new NullReferenceException("Provider Null");
                            wasapiOut = new CustomNaudio.CustomWasapiOut(_mMDeviceHelper.MMDevice, AudioClientShareMode.Shared, true, 200);
                            wasapiOut.Init(Provider);
                            wasapiOut.PlaybackStopped += PlaybackStopped;

                            switch (_playbackState)
                            {
                                case PlaybackState.Playing:
                                    wasapiOut.Play();
                                    break;
                                case PlaybackState.Paused:
                                    wasapiOut.Pause();
                                    break;
                            }
                            _audioClientCache = wasapiOut.audioClient;
                        }
                        finally
                        {
                            _state.UnLock();
                        }

                    }
                };
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
        public Exception? NewPlaying(string filePath, PlayerMode? playerMode, PlaybackState playbackState = PlaybackState.Playing, TimeSpan? position = null)
        {
            StateCheckHelper();
            Exception? result = null;
            _threadManager.Invoke(() => 
            {
                _playbackState = PlaybackState.Stopped;

                wasapiOut?.Stop(); wasapiOut?.Dispose();

                Provider?.Dispose();
                Provider = null;
                if (playbackState == PlaybackState.Stopped) return;

                try
                {
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
                    if (position != null && ((TimeSpan)position).TotalSeconds > 0)
                        Provider.CurrentTime = (TimeSpan)position;

                    wasapiOut = new CustomNaudio.CustomWasapiOut(_mMDeviceHelper.MMDevice, AudioClientShareMode.Shared, true, 200);
                    wasapiOut.Init(Provider);
                    wasapiOut.PlaybackStopped += PlaybackStopped;

                }
                catch(Exception ex)
                {
                    result = ex;
                    wasapiOut?.Stop();
                    wasapiOut?.Dispose();
                    wasapiOut = null;
                    Provider?.Dispose();
                    Provider = null;
                    return;
                }

                _playbackState = playbackState;
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
            if (result == null)
                AudioOpen?.Invoke();
            return result;
        }

        private void PlaybackStopped(object? s,StoppedEventArgs stoppedEventArgs)
        {
            if (_state.Value != PlayerState.Dispose && CurrentSeconds >= Provider.TotalTime.TotalSeconds) AudioEnd?.Invoke(); AudioStop?.Invoke();
        }

        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Skip(double seconds) => CurrentSeconds += seconds;

        AudioClient? _audioClientCache = null;
        public double CurrentSeconds
        {
            get
            {
                if (_state.Value != PlayerState.None || Provider == null) return 0;
                double currentpadding = 0;
                if (_audioClientCache == null && wasapiOut != null)
                    _threadManager.Invoke(() => { _audioClientCache = wasapiOut.audioClient; });
                if (_audioClientCache == null) return Provider.CurrentTime.TotalSeconds;

                Exception? exception = _threadManager.Invoke(() => { currentpadding = _audioClientCache.CurrentPadding; });

                if(exception != null)
                {
                    Debug.WriteLine("Re-acquire AudioClient");
                    if (_audioClientCache == null && wasapiOut != null)
                        _threadManager.Invoke(() => {
                            _audioClientCache = wasapiOut.audioClient;
                            if (_audioClientCache == null)
                                currentpadding = 0;
                            else
                                currentpadding = _audioClientCache.CurrentPadding;
                        });
                }
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
                return Provider.TotalTime;
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
