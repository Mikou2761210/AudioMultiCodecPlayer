using MikouTools.ThreadTools;
using MikouTools.UtilityTools.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static AudioMultiCodecPlayer.Player;

namespace AudioMultiCodecPlayer.Helper
{
    internal enum MMDeviceHelperState
    {
        Uninitialized,
        None,
        Dispose
    }
    internal class MMDeviceHelper : IDisposable
    {
        public Action? MMDeviceChangeStart;
        public Action? MMDeviceChangeEnd;
        public Action<double>? VolumeChange = null;
        public Action<bool>? MuteChange = null;


        LockableProperty<MMDevice> _mMdevice = new LockableProperty<MMDevice>(null!);
        ThreadManager _threadManager;
        MMDeviceEnumerator _deviceEnumerator;
        AudioSessionEventsHandler _audioSessionEventsHandler = new AudioSessionEventsHandler();
        DeviceNotificationClient _deviceNotificationClient = new DeviceNotificationClient();
        public MMDeviceHelper(ThreadManager threadManager)
        {
            _state.Lock();
            try
            {
                _threadManager = threadManager;

                _threadManager.Invoke(() =>
                {
                    _deviceEnumerator = new MMDeviceEnumerator();
                    _deviceEnumerator.RegisterEndpointNotificationCallback(_deviceNotificationClient);
                });

                if (_deviceEnumerator == null) throw new NullReferenceException(nameof(_deviceEnumerator));
                _state.AccessValueWhileLocked = MMDeviceHelperState.None;
                AudioDeviceMode = PlayerAudioDeviceMode.Auto;


                double? lastvolume = Volume;
                bool? lastmute = Mute;

                _audioSessionEventsHandler.VolumeChanged += (v, m) => { if (lastvolume != v) { lastvolume = v; VolumeChange?.Invoke(v); } if (lastmute != m) { lastmute = m; MuteChange?.Invoke(m); } };

            }
            finally
            {
                _state.UnLock();
            }
        }


        public MMDevice MMDevice { get { StateCheckHelper(); return _mMdevice.Value; } }


        PlayerAudioDeviceMode _audioDeviceMode;
        readonly object _audioDeviceModeLock = new object();
        public PlayerAudioDeviceMode AudioDeviceMode
        {
            get { StateCheckHelper(); lock (_audioDeviceModeLock) return _audioDeviceMode; }
            set
            {
                StateCheckHelper();
                lock (_audioDeviceModeLock)
                {
                    _audioDeviceMode = value;
                    switch (value)
                    {
                        case PlayerAudioDeviceMode.Auto:
                            _deviceNotificationClient.DefaultDeviceChanged = (dataflow, role, id) =>
                            {

                                if ((DataFlow)dataflow == DataFlow.Render && (Role)role == Role.Multimedia)
                                {
                                    DeviceChanged(id);
                                }
                            };
                            DeviceChanged(null);
                            break;
                        case PlayerAudioDeviceMode.Manual:
                            _deviceNotificationClient.DefaultDeviceChanged = null;
                            break;
                    }
                }
            }
        }
        public void DeviceChanged(string? mmdeviceid = null)
        {
            StateCheckHelper();
            _threadManager.Invoke(() => {
                if (mmdeviceid == null || _mMdevice.Value.ID != mmdeviceid)
                {
                    MMDevice? newDevice = null;
                    try
                    {
                        if (mmdeviceid != null)
                        {
                            newDevice = _deviceEnumerator.GetDevice(mmdeviceid);
                        }
                        else
                        {
                            newDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        }
                    }
                    catch { newDevice?.Dispose(); newDevice = null; throw; }
                    if (newDevice == null) throw new NullReferenceException();


                    MMDeviceChangeStart?.Invoke();
                    _mMdevice.Lock();
                    try
                    {
                        if (_mMdevice.AccessValueWhileLocked != null)
                        {
                            _mMdevice.AccessValueWhileLocked.AudioSessionManager.AudioSessionControl.UnRegisterEventClient(_audioSessionEventsHandler);
                            _mMdevice.AccessValueWhileLocked.Dispose();
                        }

                        _mMdevice.AccessValueWhileLocked = newDevice;

                        _mMdevice.AccessValueWhileLocked.AudioSessionManager.AudioSessionControl.RegisterEventClient(_audioSessionEventsHandler);

                    }
                    finally 
                    {
                        _mMdevice.UnLock();
                        MMDeviceChangeEnd?.Invoke();
                    }
                }
            });

        }

        public double? Volume
        {
            get { StateCheckHelper(); return _mMdevice.Value.AudioSessionManager.SimpleAudioVolume.Volume; }
            set 
            {
                StateCheckHelper();
                if (value != null && _mMdevice != null)
                {
                    if(value > 1) value = 1;
                    else if (value < 0) value = 0;
                    _mMdevice.Value.AudioSessionManager.SimpleAudioVolume.Volume = (float)value;
                }

            }
        }

        public bool? Mute
        {
            get { StateCheckHelper(); return _mMdevice.Value.AudioSessionManager.SimpleAudioVolume.Mute; }
            set { StateCheckHelper(); if (value != null && _mMdevice != null) _mMdevice.Value.AudioSessionManager.SimpleAudioVolume.Mute = (bool)value; }
        }


        private void StateCheckHelper()
        {
            switch (_state.Value)
            {
                case MMDeviceHelperState.None:
                    break;
                case MMDeviceHelperState.Dispose:
                    throw new ObjectDisposedException("MMDeviceHelper");
                case MMDeviceHelperState.Uninitialized:
                    throw new Exception("MMDeviceHelperState.Uninitialized");
            }
        }

        LockableProperty<MMDeviceHelperState> _state = new LockableProperty<MMDeviceHelperState>(MMDeviceHelperState.Uninitialized);
        public void Dispose()
        {
            if (_state.SetAndReturnOld(MMDeviceHelperState.Dispose) != MMDeviceHelperState.Dispose)
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_deviceNotificationClient);
                _mMdevice.AccessValueWhileLocked.AudioSessionManager.AudioSessionControl.UnRegisterEventClient(_audioSessionEventsHandler);
                _mMdevice.Value.Dispose();
            }
        }

    }


    internal class DeviceNotificationClient : IMMNotificationClient
    {
        // イベントの宣言
        public Action<string>? DeviceAdded;
        public Action<string>? DeviceRemoved;
        public Action<string, DeviceState>? DeviceStateChanged;
        public Action<DataFlow, Role, string>? DefaultDeviceChanged;
        public Action<string, PropertyKey>? PropertyValueChanged;
        public Action<AudioVolumeNotificationData>? VolumeNotification;

        // イベントを呼び出すメソッドの実装
        public void OnDeviceAdded(string deviceId) => DeviceAdded?.Invoke(deviceId);

        public void OnDeviceRemoved(string deviceId) => DeviceRemoved?.Invoke(deviceId);

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => DeviceStateChanged?.Invoke(deviceId, newState);

        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role role, string defaultDeviceId) => DefaultDeviceChanged?.Invoke(dataFlow, role, defaultDeviceId);

        public void OnNotify(AudioVolumeNotificationData data) => VolumeNotification?.Invoke(data);

        public void OnPropertyValueChanged(string deviceId, PropertyKey key) => PropertyValueChanged?.Invoke(deviceId, key);
    }


    internal class AudioSessionEventsHandler : IAudioSessionEventsHandler
    {
        public event Action<string>? DisplayNameChanged;
        void IAudioSessionEventsHandler.OnDisplayNameChanged(string displayName) => DisplayNameChanged?.Invoke(displayName);



        public event Action<string>? IconPathChanged;
        void IAudioSessionEventsHandler.OnIconPathChanged(string iconPath) => IconPathChanged?.Invoke(iconPath);



        public event Action<float, bool>? VolumeChanged;
        void IAudioSessionEventsHandler.OnVolumeChanged(float volume, bool isMuted) => VolumeChanged?.Invoke(volume, isMuted);



        public event Action<uint, IntPtr, uint>? ChannelVolumeChanged;
        void IAudioSessionEventsHandler.OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) => ChannelVolumeChanged?.Invoke(channelCount, newVolumes, channelIndex);



        public event Action<Guid>? GroupingParamChanged;
        void IAudioSessionEventsHandler.OnGroupingParamChanged(ref Guid groupingId) => GroupingParamChanged?.Invoke(groupingId);



        public event Action<AudioSessionDisconnectReason>? SessionDisconnected;
        void IAudioSessionEventsHandler.OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => SessionDisconnected?.Invoke(disconnectReason);



        public event Action<AudioSessionState>? StateChanged;
        void IAudioSessionEventsHandler.OnStateChanged(AudioSessionState state) => StateChanged?.Invoke(state);
    }
}
