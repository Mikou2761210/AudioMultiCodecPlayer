using NAudio.Wave;

namespace AudioMultiCodecPlayer.CustomWaveProvider
{
    public interface CustomBaseProvider : IWaveProvider, IDisposable
    {

        internal TimeSpan TotleTime { get; }

        internal TimeSpan CurrentTime { get; set; }

    }
}
