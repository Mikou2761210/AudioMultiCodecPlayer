using Concentus;
using Concentus.Oggfile;
using Concentus.Structs;
using MultiCodecPlayer.Helper;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using MultiCodecPlayer.CustomWaveProvider;
using MikouTools.CollectionTools.ThreadSafeCollections;

namespace MultiCodecPlayer.CustomWaveProvider
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



        public OpusProvider(string filename)
        {
            FileName = filename;
            this.waveFormat = new WaveFormat(SampleRate, Channels);

            opusStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            opusDecoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            opusOggRead = new OpusOggReadStream(opusDecoder, opusStream);

            int secondsdata = (SampleRate * 2/*(16 / 8)*/ * Channels);
            tempAudioDataCapacty = secondsdata * 3;
            DecodingThreshold = secondsdata * 2;
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
                return opusOggRead.CurrentTime;
            }
            set
            {
                lock (bufferLock)
                {
                    if(value > opusOggRead.TotalTime)
                        value = opusOggRead.TotalTime;
                    if (value.TotalSeconds <= 0)
                        value = TimeSpan.FromSeconds(0);
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
                        Debug.WriteLine($"1");
                        lock (DecodingLock)
                        {
                            Debug.WriteLine($"2");
                            Debug.WriteLine($" {opusOggRead.HasNextPacket}   {tempAudioData.Count} <= {tempAudioDataCapacty}");
                            while (opusOggRead.HasNextPacket && tempAudioData.Count <= tempAudioDataCapacty)
                            {
                                Debug.WriteLine($"3");
                                short[] packet = opusOggRead.DecodeNextPacket();
                                if (packet != null)
                                {
                                    Debug.WriteLine($"4");
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
                        Debug.WriteLine("wait");
                        decodingEvent.WaitOne(); // Signalを待つ
                        Debug.WriteLine("start");
                    }
                });
                decodingThread.IsBackground = true;
                decodingThread.Start();
            }
        }

        Stopwatch  stopwatch = Stopwatch.StartNew();
        public void RequestDecoding()
        {
            Debug.WriteLine(stopwatch.Elapsed);
            if(decodingThread == null || !decodingThread.IsAlive)
                StartDecodingThread();
            else
                decodingEvent.Set(); // Signalを送信
        }

        public void StopDecodingThread()
        {
            decodingThreadRunning = false;
            decodingEvent.Set(); // スレッドを停止させる
            decodingThread?.Join(); // 終了を待機
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
