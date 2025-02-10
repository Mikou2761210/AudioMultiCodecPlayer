using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiCodecPlayer.CustomWaveProvider
{
    public interface CustomBaseProvider : IWaveProvider, IDisposable
    {

        internal TimeSpan TotleTime { get; }

        internal TimeSpan CurrentTime { get; set; }

    }
}
