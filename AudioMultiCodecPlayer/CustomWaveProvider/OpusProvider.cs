using Concentus;
using Concentus.Oggfile;
using MikouTools.CollectionTools.ThreadSafeCollections;
using NAudio.Wave;
using System.Diagnostics;

namespace AudioMultiCodecPlayer.CustomWaveProvider
{
    internal class OpusProvider : CustomBaseProvider
    {
        public readonly string FileName;

        private readonly WaveFormat waveFormat;

        readonly FileStream opusStream;
        private readonly IOpusDecoder opusDecoder;
        private readonly OpusOggReadStream opusOggRead;


        readonly private object DecodingLock = new object();

        ThreadSafeList<byte> tempAudioData = new ThreadSafeList<byte>();
        int tempAudioDataCapacty;

        int DecodingThreshold;
        readonly public int SampleRate = 48000;
        readonly public int Channels = 2;
        int _secondsDataCount;


        public OpusProvider(string filename)
        {
            FileName = filename;
            this.waveFormat = new WaveFormat(SampleRate, Channels);

            opusStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            opusDecoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            opusOggRead = new OpusOggReadStream(opusDecoder, opusStream);

            _secondsDataCount = (SampleRate * 2/*(16 / 8)*/ * Channels);
            tempAudioDataCapacty = _secondsDataCount * 3;
            DecodingThreshold = _secondsDataCount * 2;
            RequestDecoding();
        }

        ~OpusProvider()
        {
            Dispose();
        }

        public WaveFormat WaveFormat => waveFormat;

        readonly object bufferLock = new object();

        public int Read(byte[] outputBuffer, int offset, int count)
        {
            lock (bufferLock)
            {
                if(tempAudioDataCapacty < count)
                {
                    tempAudioDataCapacty = count;
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

        public TimeSpan TotleTime => opusOggRead.TotalTime;
        public TimeSpan CurrentTime
        {
            get
            {
                return TimeSpan.FromSeconds(opusOggRead.CurrentTime.TotalSeconds - (tempAudioData.Count / _secondsDataCount)) ;
            }
            set
            {
                lock (bufferLock)
                {
                    if(value > opusOggRead.TotalTime)
                        value = opusOggRead.TotalTime;
                    if (value.TotalSeconds <= 0)
                        value = TimeSpan.FromMilliseconds(1);
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
                        lock (DecodingLock)
                        {
                            while (opusOggRead.HasNextPacket && tempAudioData.Count <= tempAudioDataCapacty)
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
                        decodingEvent.WaitOne();
                    }
                });
                decodingThread.IsBackground = true;
                decodingThread.Start();
            }
        }

        Stopwatch  stopwatch = Stopwatch.StartNew();
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
    }
}
