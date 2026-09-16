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

        // Add convenience properties with explicit List<> types to avoid ambiguity
        public List<int> FftIndicesList => FftIndices?.ToList() ?? new List<int>();
        public List<double> DbValuesList => DbValues?.ToList() ?? new List<double>();

        public double[] Samples => _handler.Samples;
        //public double[] CurrentSamples => _handler.CurrentBuffer;

        public event EventHandler Update;

        private readonly double _sampleRate;
        private readonly object _processLock = new object();

        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        public AudioDataAnalyzer(AudioDeviceHandler handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler), "AudioDataAnalyzer handler invalid");

            if(_handler.SamplesPerSecond < 44100)
            {
                throw new ArgumentException($"AudioDataAnalyzer SampleRate Error ({_handler.SamplesPerSecond})");
            }

            // On Windows, sample rate could be pretty much anything and FFT requires power-of-2 window size
            // So we need to pick sufficient window size based on sample rate

            _sampleRate = (double)_handler.SamplesPerSecond;
            const double minLen = 0.05; // seconds

            var fftWindowSize = 512;
            _log2 = 9;

            Logger.Debug($"AudioDataAnalyzer: Sample rate: {_sampleRate}");
            while (fftWindowSize / _sampleRate < minLen)
            {
                fftWindowSize *= 2;
                _log2 += 1;

                Logger.Debug($"AudioDataAnalyzer: FFT window size: {fftWindowSize}");
                Logger.Debug($"AudioDataAnalyzer: FFT log2: {_log2}");
                Logger.Debug($"AudioDataAnalyzer: FFT length: {fftWindowSize / _sampleRate}");
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
            lock (_processLock)
            {
                if (input == null || input.Length == 0)
                    return;

                var offset = Math.Max(0, input.Length - _fftWindowSize);
                for (int i = 0; i < _fftWindowSize; i++)
                {
                    Complex c = new Complex();
                    if (i + offset < input.Length)
                    {
                        c.X = (float)(input[offset + i] * FastFourierTransform.BlackmannHarrisWindow(i, _fftWindowSize));
                    }
                    else
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
        }

        private void ComputeDbValues(Complex[] compl, double[] tgt)
        {
            for (int i = 0; i < FftDataPoints; i++)
            {
                var c = compl[i];
                double mag = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                var db = 20 * Math.Log10(mag * mag);
                tgt[i] = double.IsInfinity(db) || double.IsNaN(db) ? double.MinValue : db;
            }
        }

        private void ProcessData2(double[] input)
        {
            lock (_processLock)
            {
                if (input == null || input.Length == 0)
                    return;

                for (int i = 0; i < _fftWindowSize; i++)
                {
                    Complex c = new Complex();
                    if (i < input.Length)
                    {
                        c.X = (float)(input[i] * FastFourierTransform.BlackmannHarrisWindow(i, _fftWindowSize));
                    }
                    else
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
        }

        public void ProcessData3(double[] input)
        {
            lock (_processLock)
            {
                if (input == null || input.Length == 0)
                    return;

                try
                {
                    double[] paddedAudio = FftSharp.Pad.ZeroPad(input);
                    System.Numerics.Complex[] fftInput = FftSharp.FFT.Forward(paddedAudio);
                    double[] fftMag = FftSharp.FFT.Power(fftInput);
                    int fft_len = Math.Min(fftMag.Length, DbValues.Length);
                    
                    Array.Copy(fftMag, DbValues, fft_len);
                    for (int i = fft_len; i < DbValues.Length; i++)
                    {
                        DbValues[i] = double.MinValue;
                    }

                    for (var i = 0; i < DbValues.Length; i++)
                    {
                        SpectrogramBuffer[SpectrogramFrameCount - 1, i] = DbValues[i];
                    }

                    Update?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error in ProcessData3");
                }
            }
        }
    }
}
