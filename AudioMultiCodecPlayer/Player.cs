using AudioMultiCodecPlayer.CustomNaudio;
using AudioMultiCodecPlayer.CustomWaveProvider;
using AudioMultiCodecPlayer.Helper;
using MikouTools.Thread.Specialized;
using MikouTools.Thread.Utils;
using MikouTools.ThreadTools.MikouTools.ThreadTools;
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
        ThreadManager _threadManager = new("Player",true, ApartmentState.MTA);

        internal CustomNaudio.CustomWasapiOut? wasapiOut;

        public Action? AudioOpen;
        public Action? AudioEnd;

        public Action<string>? Error;

        public Action<PlaybackState>? PlaybackStateChange;
        LockableProperty<PlaybackState> _playbackState = new LockableProperty<PlaybackState>(PlaybackState.Stopped);
        readonly object playbackStateLock = new object();
        public PlaybackState PlaybackState
        {
            get
            {
                StateCheckHelper();
                return _playbackState.Value;
            }
            set
            {
                StateCheckHelper();
                bool _playbackStateChange = false;
                _threadManager.Invoke(() =>
                {
                    if (_playbackState.Value != value)
                    {
                        _playbackStateChange = true;
                        _playbackState.Value = value;
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
                if (_playbackStateChange)
                {
                    PlaybackStateChange?.Invoke(value);
                }
            }
        }

        public Action<double>? VolumeChange { get { StateCheckHelper(); return _mMDeviceHelper.VolumeChange; } set { StateCheckHelper(); _mMDeviceHelper.VolumeChange = value; } }
        public Action<bool>? MuteChange { get { StateCheckHelper(); return _mMDeviceHelper.MuteChange; } set { StateCheckHelper(); _mMDeviceHelper.MuteChange = value; } }

        CustomBaseProvider? Provider;

        MMDeviceHelper _mMDeviceHelper;

        private readonly ThreadResourceManager _threadResourceManager = new();

        public Player()
        {
            using (var handle = _state.LockAndGetValue())
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                //Initialize
                _mMDeviceHelper = new MMDeviceHelper(_threadManager);
                _mMDeviceHelper.MMDeviceChangeStart += () => {
                    _state.EnterLock();
                    if (_playbackState.Value != PlaybackState.Stopped)
                    {
                        try
                        {
                            if (wasapiOut != null)
                            {
                                wasapiOut.PlaybackStopped -= PlaybackStopped;
                                wasapiOut.Stop();
                                wasapiOut.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            _state.ExitLock();
                            throw ex;
                        }
                    }
                    else
                    {
                        _state.ExitLock();
                    }
                };
                _mMDeviceHelper.MMDeviceChangeEnd += () => {
                    if (_playbackState.Value != PlaybackState.Stopped)
                    {
                        try
                        {
                            if (Provider == null) throw new NullReferenceException("Provider Null");
                            wasapiOut = new CustomNaudio.CustomWasapiOut(_mMDeviceHelper.MMDevice, AudioClientShareMode.Shared, true, 200);
                            wasapiOut.Init(Provider);
                            wasapiOut.PlaybackStopped += PlaybackStopped;

                            switch (_playbackState.Value)
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
                            _state.ExitLock();
                        }

                    }
                };
                _threadResourceManager.CleanupTaskAdd(_threadManager, _threadManager.Dispose);

                handle.Value = PlayerState.None;
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
                _playbackState.Value = PlaybackState.Stopped;

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

                _playbackState.Value = playbackState;
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
            {
                AudioOpen?.Invoke();
                PlaybackStateChange?.Invoke(playbackState);
            }
            return result;
        }

        private void PlaybackStopped(object? s,StoppedEventArgs stoppedEventArgs)
        {
            _playbackState.Value = PlaybackState.Stopped;
            Task.Run(() => {
                bool audioEnd = Provider != null && (CurrentSeconds >= Provider.TotalTime.TotalSeconds);

                if (_state.Value != PlayerState.Dispose) PlaybackState = PlaybackState.Paused;
                if (audioEnd) AudioEnd?.Invoke();
            });
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
                if (_state.Value != PlayerState.None || Provider == null)
                    return 0;

                EnsureAudioClient();

                double currentPadding = GetCurrentPaddingSafely();
                return Provider.CurrentTime.TotalSeconds - (currentPadding / (double)Provider.WaveFormat.SampleRate);
            }
            set
            {
                if (_state.Value != PlayerState.None || Provider == null)
                    return;
                Provider.CurrentTime = TimeSpan.FromSeconds(value);
            }
        }

        /// <summary>
        /// AudioClientの取得・再取得を行う
        /// </summary>
        private void EnsureAudioClient()
        {
            if (_audioClientCache == null && wasapiOut != null)
            {
                _threadManager.Invoke(() =>
                {
                    _audioClientCache = wasapiOut.audioClient;
                });
            }
        }

        /// <summary>
        /// 現在のpadding値を安全に取得する。失敗時は再取得を試み、失敗すれば0を返す。
        /// </summary>
        private double GetCurrentPaddingSafely()
        {
            double padding = 0; 
            Exception? result = _threadManager.Invoke(() =>
            {
                if (wasapiOut == null || wasapiOut.PlaybackState == PlaybackState.Stopped || _playbackState.Value == PlaybackState.Stopped) return;
                padding = _audioClientCache?.CurrentPadding ?? 0;
            });
            if(result != null)
            {
                Debug.WriteLine("CurrentPadding Error: " + result.Message);

                if (wasapiOut != null)
                {
                    _threadManager.Invoke(() =>
                    {
                        _audioClientCache = wasapiOut.audioClient;
                        padding = _audioClientCache?.CurrentPadding ?? 0;
                    });
                }
            }
            return padding;
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
