using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MultiCodecPlayer.CustomWaveProvider;

namespace MultiCodecPlayer.CustomWaveProvider
{
    public class CustomAudioFileReader : WaveStream, CustomBaseProvider
    {
        private WaveStream readerStream;

        private readonly SampleChannel sampleChannel;

        private readonly int destBytesPerSample;

        private readonly int sourceBytesPerSample;

        private readonly long length;

        private readonly object lockObject;

        //
        // 概要:
        //     File Name
        public string FileName { get; }

        //
        // 概要:
        //     WaveFormat of this stream
        public override WaveFormat WaveFormat => sampleChannel.WaveFormat;

        //
        // 概要:
        //     Length of this stream (in bytes)
        public override long Length => length;

        //
        // 概要:
        //     Position of this stream (in bytes)
        public override long Position
        {
            get
            {
                return SourceToDest(readerStream.Position);
            }
            set
            {
                lock (lockObject)
                {
                    readerStream.Position = DestToSource(value);
                }
            }
        }

        // 概要:
        //     Gets or Sets the Volume of this AudioFileReader. 1.0f is full volume
        public float Volume
        {
            get
            {
                return sampleChannel.Volume;
            }
            set
            {
                sampleChannel.Volume = value;
            }
        }

        TimeSpan CustomBaseProvider.TotleTime { get { return base.TotalTime; } }

        //
        // 概要:
        //     Initializes a new instance of AudioFileReader
        //
        // パラメーター:
        //   fileName:
        //     The file to open
        public CustomAudioFileReader(string fileName)
        {
            lockObject = new object();
            FileName = fileName;
            CreateReaderStream(fileName);
            sourceBytesPerSample = readerStream.WaveFormat.BitsPerSample / 8 * readerStream.WaveFormat.Channels;
            sampleChannel = new SampleChannel(readerStream, forceStereo: false);
            destBytesPerSample = 4 * sampleChannel.WaveFormat.Channels;
            length = SourceToDest(readerStream.Length);
        }

        //
        // 概要:
        //     Creates the reader stream, supporting all filetypes in the core NAudio library,
        //     and ensuring we are in PCM format
        //
        // パラメーター:
        //   fileName:
        //     File Name
        private void CreateReaderStream(string fileName)
        {
            if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                readerStream = new WaveFileReader(fileName);
                if (readerStream.WaveFormat.Encoding != WaveFormatEncoding.Pcm && readerStream.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                {
                    readerStream = WaveFormatConversionStream.CreatePcmStream(readerStream);
                    readerStream = new BlockAlignReductionStream(readerStream);
                }
            }
            else if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                if (Environment.OSVersion.Version.Major < 6)
                {
                    readerStream = new Mp3FileReader(fileName);
                }
                else
                {
                    readerStream = new MediaFoundationReader(fileName);
                }
            }
            else if (fileName.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".aif", StringComparison.OrdinalIgnoreCase))
            {
                readerStream = new AiffFileReader(fileName);
            }
            else
            {
                readerStream = new MediaFoundationReader(fileName);
            }
        }

        //
        // 概要:
        //     Reads from this wave stream
        //
        // パラメーター:
        //   buffer:
        //     Audio buffer
        //
        //   offset:
        //     Offset into buffer
        //
        //   count:
        //     Number of bytes required
        //
        // 戻り値:
        //     Number of bytes read
        public override int Read(byte[] buffer, int offset, int count)
        {
            WaveBuffer waveBuffer = new WaveBuffer(buffer);
            int count2 = count / 4;
            return Read(waveBuffer.FloatBuffer, offset / 4, count2) * 4;
        }

        //
        // 概要:
        //     Reads audio from this sample provider
        //
        // パラメーター:
        //   buffer:
        //     Sample buffer
        //
        //   offset:
        //     Offset into sample buffer
        //
        //   count:
        //     Number of samples required
        //
        // 戻り値:
        //     Number of samples read
        public int Read(float[] buffer, int offset, int count)
        {
            lock (lockObject)
            {
                return sampleChannel.Read(buffer, offset, count);
            }
        }

        //
        // 概要:
        //     Helper to convert source to dest bytes
        private long SourceToDest(long sourceBytes)
        {
            return destBytesPerSample * (sourceBytes / sourceBytesPerSample);
        }

        //
        // 概要:
        //     Helper to convert dest to source bytes
        private long DestToSource(long destBytes)
        {
            return sourceBytesPerSample * (destBytes / destBytesPerSample);
        }

        //
        // 概要:
        //     Disposes this AudioFileReader
        //
        // パラメーター:
        //   disposing:
        //     True if called from Dispose
        protected override void Dispose(bool disposing)
        {
            if (disposing && readerStream != null)
            {
                readerStream.Dispose();
                readerStream = null;
            }

            base.Dispose(disposing);
        }
    }
}
