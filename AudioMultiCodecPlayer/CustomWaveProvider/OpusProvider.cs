using Concentus;
using Concentus.Oggfile;
using Concentus.Structs;
using MikouTools.Thread.ThreadSafe.Collections;
using NAudio.Wave;
using System.Diagnostics;

namespace AudioMultiCodecPlayer.CustomWaveProvider
{
    /*internal class oldOpusProvider : CustomBaseProvider
    {
        public readonly string FileName;

        private readonly WaveFormat waveFormat;

        private FileStream opusStream;
        private IOpusDecoder opusDecoder;
        private OpusOggReadStream opusOggRead;


        //readonly private object DecodingLock = new object();

        ThreadSafeList<List<byte>,byte> tempAudioData = new ThreadSafeList<List<byte>, byte>([]);
        int tempAudioDataCapacity;

        int DecodingThreshold;
        readonly public int SampleRate = 48000;
        readonly public int Channels = 2;
        //int _secondsDataCount;


        public OpusProvider(string filename)
        {
            FileName = filename;
            this.waveFormat = new WaveFormat(SampleRate,16, Channels);

            opusStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            opusDecoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            opusOggRead = new OpusOggReadStream(opusDecoder, opusStream);

            //_secondsDataCount = this.waveFormat.AverageBytesPerSecond;
            tempAudioDataCapacity = this.waveFormat.AverageBytesPerSecond * 3;
            DecodingThreshold = this.waveFormat.AverageBytesPerSecond * 2;
            RequestDecoding();
        }

        ~OpusProvider()
        {
            Dispose();
        }

        public WaveFormat WaveFormat => waveFormat;

        readonly object bufferLock = new object();
        bool _resetFlag = false;

        public int Read(byte[] outputBuffer, int offset, int count)
        {
            lock (bufferLock)
            {
                if(tempAudioDataCapacity < count)
                {
                    tempAudioDataCapacity = count;
                    tempAudioData.Capacity = count;
                }

                if (tempAudioData.Count <= DecodingThreshold)
                    RequestDecoding();

                while (tempAudioData.Count < count)
                {
                    if (!opusOggRead.HasNextPacket)
                    {
                        count = tempAudioData.Count;
                        break;
                    }
                    Thread.Sleep(10);
                }

                for (int i = 0; i < count; i++)
                {
                    outputBuffer[offset + i] = tempAudioData[i];
                }

                tempAudioData.RemoveRange(0, count);


                return count;
            }
        }



        //TimeSpan CustomBaseProvider.TotleTime => opusOggRead.TotalTime;

        public TimeSpan TotalTime => opusOggRead.TotalTime;
        readonly object _currentTimeLock = new object();
        public TimeSpan CurrentTime
        {
            get
            {
                lock (_currentTimeLock)
                {

                    
                    return TimeSpan.FromSeconds(opusOggRead.CurrentTime.TotalSeconds - ((double)tempAudioData.Count / (double)this.waveFormat.AverageBytesPerSecond));
                }
            }
            set
            {
                lock (bufferLock)
                {
                    if(value > opusOggRead.TotalTime)
                        value = opusOggRead.TotalTime;
                    if (value.TotalSeconds <= 0)
                        value = TimeSpan.FromMilliseconds(1);

                    if (_resetFlag)
                    {
                        _resetFlag = false;
                        opusDecoder.ResetState();
                        opusOggRead = new OpusOggReadStream(opusDecoder, opusStream);
                        //opusOggRead.SeekTo(TimeSpan.FromMilliseconds(1));
                    }
                    opusOggRead.SeekTo(value);
                    tempAudioData.Clear();
                    RequestDecoding();
                }
            }
        }


        public void Seek(double Seconds) => Seek(TimeSpan.FromSeconds(Seconds));

        public void Seek(TimeSpan timeSpan)
        {
            CurrentTime = timeSpan;
        }




        private Thread? decodingThread;
        private AutoResetEvent decodingEvent = new AutoResetEvent(false);
        private volatile bool decodingThreadRunning = true;

        public void StartDecodingThread()
        {
            if (decodingThread == null || !decodingThread.IsAlive)
            {
                decodingThreadRunning = true;
                decodingThread = new Thread(() =>
                {
                    while (decodingThreadRunning)
                    {
                        //lock (DecodingLock)
                        {

                            while (tempAudioData.Count <= tempAudioDataCapacity)
                            {
                                if (!opusOggRead.HasNextPacket)
                                {
                                    _resetFlag = true;
                                    break;
                                }
                                lock (_currentTimeLock)
                                {
                                    short[] packet = opusOggRead.DecodeNextPacket();
                                    if (packet != null)
                                    {
                                        byte[] packetBytes = new byte[packet.Length * 2];
                                        Buffer.BlockCopy(packet, 0, packetBytes, 0, packetBytes.Length);
                                        tempAudioData.AddRange(packetBytes);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("DecodeNextPacket returned null.");
                                    }
                                }

                            }
                        }
                        decodingEvent.WaitOne();
                    }
                })
                { Name = "DecodingThread" };
                decodingThread.IsBackground = true;
                decodingThread.Start();
            }
        }

        public void RequestDecoding()
        {
            if(decodingThread == null || !decodingThread.IsAlive)
                StartDecodingThread();
            else
                decodingEvent.Set();
        }

        public void StopDecodingThread()
        {
            decodingThreadRunning = false;
            decodingEvent.Set(); 
            decodingThread?.Join();
        }



        bool dispose = false;
        public void Dispose()
        {
            if (!dispose)
            {
                dispose = true;
                StopDecodingThread();
                opusStream?.Dispose();
                opusDecoder?.Dispose();
            }

        }
    }*/


    internal class OpusProvider : CustomBaseProvider
    {
        public readonly string FileName;

        private readonly WaveFormat waveFormat;

        private FileStream opusStream;
        private IOpusDecoder opusDecoder;
        private OpusOggReadStream opusOggRead;


        //readonly private object DecodingLock = new object();

        MikouTools.Collections.Signaling.CountSignalingQueue<byte> tempAudioData;

        readonly public int SampleRate = 48000;
        readonly public int Channels = 2;

        int _decodingPausedCount;
        int _decodeResumed;
        public OpusProvider(string filename)
        {
            FileName = filename;
            this.waveFormat = new WaveFormat(SampleRate, 16, Channels);

            opusStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            opusDecoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            opusOggRead = new OpusOggReadStream(opusDecoder, opusStream);
            _decodingPausedCount = this.waveFormat.AverageBytesPerSecond * 2;
            _decodeResumed = this.waveFormat.AverageBytesPerSecond;
            tempAudioData = new(count => count >= _decodingPausedCount, count => count <= _decodeResumed);
            RequestDecoding();
        }

        ~OpusProvider()
        {
            Dispose();
        }

        public WaveFormat WaveFormat => waveFormat;

        readonly object bufferLock = new();
        bool _resetFlag = false;

        public int Read(byte[] outputBuffer, int offset, int count)
        {
            lock (bufferLock)
            {
                if (_decodeResumed <= count)
                {
                    _decodeResumed = count;
                    _decodingPausedCount = count * 2;
                    tempAudioData.RecheckCount();
                }


                while (tempAudioData.Count < count)
                {
                    if (!opusOggRead.HasNextPacket)
                    {
                        count = tempAudioData.Count;
                        break;
                    }
                    else if(!decodingThreadRunning)
                    {
                        count = 0;
                        break;
                    }
                    Thread.Sleep(10);
                }

                for (int i = 0; i < count; i++)
                {
                    outputBuffer[offset + i] = tempAudioData.Dequeue();
                }
                Debug.WriteLine($"return:{count}");

                return count;
            }
        }



        //TimeSpan CustomBaseProvider.TotleTime => opusOggRead.TotalTime;

        public TimeSpan TotalTime => opusOggRead.TotalTime;
        readonly object _currentTimeLock = new object();
        public TimeSpan CurrentTime
        {
            get
            {
                lock (_currentTimeLock)
                {


                    return TimeSpan.FromSeconds(opusOggRead.CurrentTime.TotalSeconds - ((double)tempAudioData.Count / (double)this.waveFormat.AverageBytesPerSecond));
                }
            }
            set
            {
                lock (bufferLock)
                {
                    if (value > opusOggRead.TotalTime)
                        value = opusOggRead.TotalTime;
                    if (value.TotalSeconds <= 0)
                        value = TimeSpan.FromMilliseconds(1);

                    
                    if (_resetFlag)
                    {
                        _resetFlag = false;
                        opusDecoder.ResetState();
                        opusOggRead = new OpusOggReadStream(opusDecoder, opusStream);
                        opusOggRead.SeekTo(value);
                        tempAudioData.Clear();
                        tempAudioData.EnableWait();
                    }
                    else
                    {
                        opusOggRead.SeekTo(value);
                        tempAudioData.Clear();
                    }
                    //RequestDecoding();
                }
            }
        }


        public void Seek(double Seconds) => Seek(TimeSpan.FromSeconds(Seconds));

        public void Seek(TimeSpan timeSpan)
        {
            CurrentTime = timeSpan;
        }




        private Thread? decodingThread;
        private volatile bool decodingThreadRunning = true;

        public void StartDecodingThread()
        {
            if (decodingThread == null || !decodingThread.IsAlive)
            {
                decodingThreadRunning = true;
                decodingThread = new Thread(() =>
                {
                    while (decodingThreadRunning)
                    {
                        if (!opusOggRead.HasNextPacket && !_resetFlag)
                        {
                            _resetFlag = true;
                            tempAudioData.AlwaysWait();
                        }
                        else
                        {
                            lock (_currentTimeLock)
                            {
                                short[] packet = opusOggRead.DecodeNextPacket();
                                if (packet != null)
                                {
                                    byte[] packetBytes = new byte[packet.Length * 2];
                                    Buffer.BlockCopy(packet, 0, packetBytes, 0, packetBytes.Length);

                                    foreach (byte packetByte in packetBytes)
                                        tempAudioData.Enqueue(packetByte);
                                }
                                else
                                {
                                    Debug.WriteLine("DecodeNextPacket returned null.");
                                }
                            }
                        }
                        tempAudioData.CountCheckAndWait();
                    }
                })
                { Name = "DecodingThread" };
                decodingThread.IsBackground = true;
                decodingThread.Start();
            }
        }

        public void RequestDecoding()
        {
            if (decodingThread == null || !decodingThread.IsAlive)
                StartDecodingThread();
        }

        public void StopDecodingThread()
        {
            decodingThreadRunning = false;
            tempAudioData.DisableWait();
            decodingThread?.Join();
        }



        bool dispose = false;
        public void Dispose()
        {
            if (!dispose)
            {
                dispose = true;
                StopDecodingThread();
                opusStream?.Dispose();
                opusDecoder?.Dispose();
            }

        }
    }
}
