using NAudio.Dsp;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioVisualizerWidget
{
    class AudioDataAnalyzer
    {
        private readonly AudioDeviceHandler _handler;

        private int _fftWindowSize;
        private int _log2;
        private Complex[] _fftInput;
        public double[] DbValues { get; private set; }
        public double[,] SpectrogramBuffer { get; }
        public const int SpectrogramFrameCount = 1;

        public int FftDataPoints { get; }
        public int FftFrequencySpacing { get; }

        //These are used to improve Append() perf
        public int[] PrimaryIndices { get; }
        public int[] FftIndices { get; }

        public double[] Samples => _handler.Samples;
        //public double[] CurrentSamples => _handler.CurrentBuffer;

        public event EventHandler Update;

        private double sampleRate;

        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        public AudioDataAnalyzer(AudioDeviceHandler handler)
        {
            _handler = handler;

            if(_handler == null) {
                throw new Exception("AudioDataAnalyzer handler invalid");
            }

            if(_handler.SamplesPerSecond < 44100)
            {
                throw new Exception($"AudioDataAnalyzer SampleRate Error ({_handler.SamplesPerSecond})");
            }

            // On Windows, sample rate could be pretty much anything and FFT requires power-of-2 window size
            // So we need to pick sufficient window size based on sample rate

            sampleRate = (double)_handler.SamplesPerSecond;
            const double minLen = 0.05; // seconds

            var fftWindowSize = 512;
            _log2 = 9;

            Logger.Debug($"AudioDataAnalyzer: Sample rate: {sampleRate}");
            while (fftWindowSize / sampleRate < minLen)
            {
                fftWindowSize *= 2;
                _log2 += 1;

                Logger.Debug($"AudioDataAnalyzer: FFT window size: {fftWindowSize}");
                Logger.Debug($"AudioDataAnalyzer: FFT log2: {_log2}");
                Logger.Debug($"AudioDataAnalyzer: FFT length: {fftWindowSize / sampleRate}");
            }

            Logger.Debug($"AudioDataAnalyzer: FFT window size: {fftWindowSize}");
            Logger.Debug($"AudioDataAnalyzer: FFT log2: {_log2}");

            _fftWindowSize = fftWindowSize;

            _fftInput = new Complex[_fftWindowSize];
            FftDataPoints = fftWindowSize / 2;
            FftFrequencySpacing = _handler.SamplesPerSecond / fftWindowSize;

            Logger.Debug("AudioDataAnalyzer: Populating FFT Indices");
            FftIndices = new int[FftDataPoints];
            for (int i = 0; i < FftIndices.Length; i++)
            {
                FftIndices[i] = i * FftFrequencySpacing;
            }

            Logger.Debug("AudioDataAnalyzer: Populating Spectrogram Buffer");
            SpectrogramBuffer = new double[SpectrogramFrameCount, FftDataPoints];
            for (int x = 0; x < SpectrogramFrameCount; x++)
            {
                for (int y = 0; y < FftDataPoints; y++)
                {
                    SpectrogramBuffer[x, y] = double.MinValue;
                }
            }
            DbValues = new double[FftDataPoints];

            Logger.Debug("AudioDataAnalyzer: Populating Primary Indices");
            PrimaryIndices = new int[handler.BufferSize];
            for (int i = 0; i < PrimaryIndices.Length; i++)
            {
                PrimaryIndices[i] = i;
            }

            Logger.Debug("AudioDataAnalyzer: Hooking data received event");
            handler.DataReceived += DataReceived;
        }

        private void DataReceived(object sender, EventArgs e)
        {
            ProcessData2(_handler.Samples);
        }

        private void ProcessData(double[] input)
        {
            var offset = input.Length - _fftWindowSize;
            for (int i = 0; i < _fftWindowSize; i++)
            {
                Complex c = new Complex();
                c.X = (float)(input[offset + i] * FastFourierTransform.BlackmannHarrisWindow(i, _fftWindowSize));
                c.Y = 0;
                _fftInput[i] = c;
            }

            FastFourierTransform.FFT(true, _log2, _fftInput);

            ComputeDbValues(_fftInput, DbValues);

            Array.Copy(SpectrogramBuffer, FftDataPoints, SpectrogramBuffer, 0, (SpectrogramFrameCount - 1) * FftDataPoints);
            for (var i = 0; i < FftDataPoints; i++)
            {
                SpectrogramBuffer[SpectrogramFrameCount - 1, i] = DbValues[i];
            }

            Update?.Invoke(this, EventArgs.Empty);
        }

        private void ComputeDbValues(Complex[] compl, double[] tgt)
        {
            for (int i = 0; i < FftDataPoints; i++)
            {
                var c = compl[i];
                double mag = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                var db = 20 * Math.Log10(mag*mag);
                tgt[i] = db;
            }
        }


        private void ProcessData2(double[] input)
        {
            for (int i = 0; i < _fftWindowSize; i++)
            {
                Complex c = new Complex();
                if (i < input.Length)
                {
                    c.X = (float)(input[i] * FastFourierTransform.BlackmannHarrisWindow(i, _fftWindowSize));
                } else
                {
                    c.X = 0;
                }
                c.Y = 0;
                _fftInput[i] = c;
            }

            FastFourierTransform.FFT(true, _log2, _fftInput);

            ComputeDbValues(_fftInput, DbValues);

            Array.Copy(SpectrogramBuffer, FftDataPoints, SpectrogramBuffer, 0, (SpectrogramFrameCount - 1) * FftDataPoints);
            for (var i = 0; i < FftDataPoints; i++)
            {
                SpectrogramBuffer[SpectrogramFrameCount - 1, i] = DbValues[i];
            }

            Update?.Invoke(this, EventArgs.Empty);
        }

        public void ProcessData3(double[] input)
        {

            double[] paddedAudio = FftSharp.Pad.ZeroPad(input);
            System.Numerics.Complex[] _fftInput = FftSharp.FFT.Forward(paddedAudio);
            double[] fftMag = FftSharp.FFT.Power(_fftInput);
            int fft_len = fftMag.Length > DbValues.Length ? DbValues.Length : fftMag.Length;
            
            Array.Copy(fftMag, DbValues, fft_len);
            for(int i = fft_len; i < DbValues.Length; i++)
            {
                DbValues[i] = double.MinValue;
            }

            for (var i = 0; i < DbValues.Length; i++)
            {
                SpectrogramBuffer[SpectrogramFrameCount - 1, i] = DbValues[i];
            }

            /* Array.Copy(SpectrogramBuffer, FftDataPoints, SpectrogramBuffer, 0, (SpectrogramFrameCount - 1) * FftDataPoints);
             for (var i = 0; i < FftDataPoints; i++)
             {
                 SpectrogramBuffer[SpectrogramFrameCount - 1, i] = DbValues[i];
             }*/

            Update?.Invoke(this, EventArgs.Empty);

        }

    }
}
