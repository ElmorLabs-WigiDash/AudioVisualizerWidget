using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioVisualizerWidget
{
    public class WasapiLoopbackCaptureAlt : WasapiCapture
    {
        public WasapiLoopbackCaptureAlt(MMDevice device) : base(device) { }

        public override WaveFormat WaveFormat
        {
            get => base.WaveFormat;
            set => base.WaveFormat = value;
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        {
            return AudioClientStreamFlags.Loopback;
        }
    }
}
