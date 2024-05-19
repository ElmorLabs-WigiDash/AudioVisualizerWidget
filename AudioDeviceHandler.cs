using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using WigiDashWidgetFramework;

namespace AudioVisualizerWidget
{
    public class AudioDeviceHandler : IDisposable
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private CancellationToken _token;
        private AutoResetEvent _processEvt = new AutoResetEvent(false);
        private WasapiLoopbackCaptureAlt _capture;
        private MMDevice _device;
        private WaveFormat _waveFormat;
        private SampleReader _reader;
        private readonly double[] _input;
        private readonly double[] _inputBack;
        //private double[] _currentBuffer;

        private WaveOutEvent _waveOut;

        public int SamplesPerSecond { get; }
        public int BufferSize { get; }

        public double[] Samples => _inputBack;
        //public double[] CurrentBuffer => _currentBuffer;

        private int SampleCount = 0;

        public event EventHandler DataReceived;

        private ILogger _logger;

        public AudioDeviceHandler(MMDevice device, ILogger logger)
        {
            _logger = logger;

            _logger.Debug($"AudioDeviceHandler: Device: {device.FriendlyName}");
            _token = _cts.Token;
            _device = device;

            _waveFormat = GetSupportedWaveFormat(device);

            if (_waveFormat == null)
            {
                _logger.Error("Could not get a supported waveformat!");
                return;
            }

            SamplesPerSecond = _waveFormat.SampleRate;

            _logger.Debug($"AudioDeviceHandler: Sample rate: {SamplesPerSecond}");

            BufferSize = SamplesPerSecond * 50 / 1000; // 50ms buffer

            _logger.Debug($"AudioDeviceHandler: Buffer size: {BufferSize}");
            _logger.Debug($"Derived Waveform: {_waveFormat}");

            _input = new double[BufferSize];
            _inputBack = new double[BufferSize];

            //_currentBuffer = new double[(int)(BufferSize)];

            _logger.Debug("AudioDeviceHandler: Hooking into audio device...");

            var capture = new WasapiLoopbackCaptureAlt(device);
            capture.WaveFormat = _waveFormat;
            capture.ShareMode = AudioClientShareMode.Shared;
            capture.DataAvailable += DataAvailable;
            capture.RecordingStopped += RecordingStopped;
            _capture = capture;

            _reader = new SampleReader(_waveFormat);

            // Play silence to initialize the audio device
            _logger.Debug("AudioDeviceHandler: Playing Silence...");
            var silence = new SilenceProvider(_waveFormat).ToSampleProvider();
            _waveOut = new WaveOutEvent();
            _waveOut.Init(silence);
            _waveOut.Play();

            _logger.Debug("AudioDeviceHandler: Starting audio capture...");
            _ = Task.Run(ProcessData, _cts.Token);
        }

        private List<WaveFormat> SupportedFormats = new List<WaveFormat>
        {
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 2),
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)/*,
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 6),
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 6),
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 8),
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 8)*/
        };

        private WaveFormat GetSupportedWaveFormat(MMDevice device)
        {
            WaveFormat deviceMixFormat = device.AudioClient.MixFormat;
            WaveFormat returnFormat = null;

            _logger.Debug($"GetSupportedWaveFormat: Device MixFormat: {deviceMixFormat}");

            int i = 0;
            while(returnFormat == null && i<SupportedFormats.Count)
            {
                WaveFormat testFormat = SupportedFormats[i];
                if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, testFormat))
                {
                    returnFormat = testFormat;
                    break;
                }
                i++;
            }

            if(returnFormat == null)
            {
                _logger.Error("GetSupportedWaveFormat: Could not find a supported format, trying exclusive mode");
                i = 0;
                while (returnFormat == null && i < SupportedFormats.Count)
                {
                    WaveFormat testFormat = SupportedFormats[i];
                    if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, testFormat))
                    {
                        returnFormat = testFormat;
                        break;
                    }
                    i++;
                }
            }

            return returnFormat;
        }

        public void Start()
        {
            if (_capture.CaptureState == CaptureState.Stopped)
            {
                _logger.Debug("AudioDeviceHandler: Starting Capture...");
                _capture?.StartRecording();
                _waveOut?.Play();
            }
        }

        public void Stop()
        {
            if (_capture?.CaptureState == CaptureState.Capturing)
            {
                _logger.Debug("AudioDeviceHandler: Stopping Capture...");
                _capture?.StopRecording();
                _waveOut?.Stop();
            }
        }

        private void ProcessData()
        {
            while (!_token.IsCancellationRequested)
            {
                if (_processEvt.WaitOne(20))
                {
                    lock (_input)
                    {
                        SampleCount = 0;
                        Array.Copy(_input, _inputBack, _input.Length);
                        //Array.Copy(_input, _input.Length - _currentBuffer.Length - 1, _currentBuffer, 0, _currentBuffer.Length);
                    }
                    DataReceived?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (_token.IsCancellationRequested)
            {
                _capture?.Dispose();
                _device?.Dispose();
            }
        }

        private void DataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            // We need to get off this thread ASAP to avoid losing frames
            lock (_input)
            {
                _reader.ReadSamples(e.Buffer, e.BytesRecorded, _input);
            }
            SampleCount += e.BytesRecorded;
            if (SampleCount > BufferSize / 2)
            {
                _processEvt.Set();
                SampleCount = 0;
            }
        }

        public void Dispose()
        {
            Stop();

            _capture?.Dispose();
            _device?.Dispose();
            
            _waveOut?.Dispose();

            try { _cts?.Cancel(); }
            catch (Exception ex) { _logger.Error(ex, "Could not cancel capture task"); }
        }
    }
}
