using NAudio.CoreAudioApi;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace AudioVisualizerWidget
{
    public class AudioDeviceHandler : IDisposable
    {
        private CancellationTokenSource _cts;
        private CancellationToken _token;
        private AutoResetEvent _processEvt;
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
        private bool _disposed = false;
        private readonly object _deviceLock = new object();
        private readonly object _dataLock = new object();

        public event EventHandler DataReceived;

        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        // Extended list of supported formats - added higher sample rates and channel counts
        private List<WaveFormat> SupportedFormats = new List<WaveFormat>
        {
            // Primary formats - most common
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 2),
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 2),
            
            // Higher bit-depth formats
            WaveFormat.CreateIeeeFloatWaveFormat(96000, 2),
            WaveFormat.CreateIeeeFloatWaveFormat(192000, 2),
            
            // Multi-channel options
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 6),
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 6),
            WaveFormat.CreateIeeeFloatWaveFormat(96000, 6),
            
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 8),
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 8),
            
            // Fallback options
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 1),
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)
        };

        public AudioDeviceHandler(MMDevice device)
        {
            Logger.Debug($"AudioDeviceHandler: Device: {device.FriendlyName}");
            _cts = new CancellationTokenSource();
            _token = _cts.Token;
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _processEvt = new AutoResetEvent(false);

            _waveFormat = GetSupportedWaveFormat(device);

            if (_waveFormat == null)
            {
                Logger.Error("Could not get a supported waveformat!");
                throw new InvalidOperationException("No supported wave format found for this device");
            }

            SamplesPerSecond = _waveFormat.SampleRate;

            Logger.Debug($"AudioDeviceHandler: Sample rate: {SamplesPerSecond}");

            // Adjust buffer size based on sample rate to maintain consistent time window
            BufferSize = SamplesPerSecond * 50 / 1000; // 50ms buffer

            Logger.Debug($"AudioDeviceHandler: Buffer size: {BufferSize}");
            Logger.Debug($"Derived Waveform: {_waveFormat}");

            _input = new double[BufferSize];
            _inputBack = new double[BufferSize];
            
            //_currentBuffer = new double[(int)(BufferSize)];

            Logger.Debug("AudioDeviceHandler: Hooking into audio device...");

            var capture = new WasapiLoopbackCaptureAlt(device);
            capture.WaveFormat = _waveFormat;
            capture.ShareMode = AudioClientShareMode.Shared;
            capture.DataAvailable += DataAvailable;
            capture.RecordingStopped += RecordingStopped;
            _capture = capture;

            _reader = new SampleReader(_waveFormat);

            // Play silence to initialize the audio device
            Logger.Debug("AudioDeviceHandler: Playing Silence...");
            InitializeSilenceOutput();

            Logger.Debug("AudioDeviceHandler: Starting audio capture...");
            _ = Task.Run(ProcessData, _cts.Token);
        }

        private void InitializeSilenceOutput()
        {
            try
            {
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                }

                var silence = new SilenceProvider(_waveFormat).ToSampleProvider();
                _waveOut = new WaveOutEvent();
                _waveOut.Init(silence);
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing silence output");
            }
        }

        private WaveFormat GetSupportedWaveFormat(MMDevice device)
        {
            if (device == null || device.AudioClient == null)
            {
                Logger.Error("GetSupportedWaveFormat: Device or AudioClient is null");
                return null;
            }

            WaveFormat deviceMixFormat = device.AudioClient.MixFormat;
            WaveFormat returnFormat = null;

            Logger.Debug($"GetSupportedWaveFormat: Device MixFormat: {deviceMixFormat}");
            
            // First try to match the device's own format with compatible parameters
            try
            {
                // Try to create a IEEE float format that matches the device's native sample rate and channels
                var matchedFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                    deviceMixFormat.SampleRate, 
                    deviceMixFormat.Channels
                );
                
                Logger.Debug($"Trying format matching device's native rate/channels: {matchedFormat}");
                
                if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, matchedFormat))
                {
                    Logger.Debug("Device supports matched format!");
                    return matchedFormat;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creating matched format");
            }

            try
            {
                // First try shared mode with all supported formats
                Logger.Debug("Trying shared mode formats...");
                foreach (var format in SupportedFormats)
                {
                    Logger.Debug($"Testing format: {format.SampleRate}Hz, {format.Channels} channels");
                    if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, format))
                    {
                        returnFormat = format;
                        Logger.Debug($"Found compatible format: {format.SampleRate}Hz, {format.Channels} channels");
                        break;
                    }
                }

                // If shared mode failed, try exclusive mode
                if (returnFormat == null)
                {
                    Logger.Debug("No shared mode format supported, trying exclusive mode...");
                    foreach (var format in SupportedFormats)
                    {
                        if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format))
                        {
                            returnFormat = format;
                            Logger.Debug($"Found compatible exclusive format: {format.SampleRate}Hz, {format.Channels} channels");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding supported wave format");
            }

            return returnFormat;
        }

        public void Start()
        {
            if (_disposed) return;
            
            lock (_deviceLock)
            {
                if (_capture?.CaptureState == CaptureState.Stopped)
                {
                    Logger.Debug("AudioDeviceHandler: Starting Capture...");
                    try
                    {
                        _capture?.StartRecording();
                        _waveOut?.Play();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error starting capture");
                    }
                }
            }
        }

        public void Stop()
        {
            if (_disposed) return;
            
            lock (_deviceLock)
            {
                if (_capture?.CaptureState == CaptureState.Capturing)
                {
                    Logger.Debug("AudioDeviceHandler: Stopping Capture...");
                    try
                    {
                        _capture?.StopRecording();
                        _waveOut?.Stop();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error stopping capture");
                    }
                }
            }
        }

        private void ProcessData()
        {
            while (!_token.IsCancellationRequested)
            {
                try
                {
                    if (_processEvt.WaitOne(20))
                    {
                        lock (_dataLock)
                        {
                            SampleCount = 0;
                            Array.Copy(_input, _inputBack, _input.Length);
                            //Array.Copy(_input, _input.Length - _currentBuffer.Length - 1, _currentBuffer, 0, _currentBuffer.Length);
                        }
                        DataReceived?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    if (!_token.IsCancellationRequested)
                    {
                        Logger.Error(ex, "Error in ProcessData");
                    }
                }
            }
        }

        private void RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error(e.Exception, "Recording stopped with exception");
            }

            if (_token.IsCancellationRequested)
            {
                Logger.Debug("Recording stopped due to cancellation request");
            }
        }

        private void DataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0 || _disposed) return;

            try
            {
                // We need to get off this thread ASAP to avoid losing frames
                lock (_dataLock)
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
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in DataAvailable");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _disposed = true;
                
                try
                {
                    Stop();
                }
                catch { }

                try
                {
                    if (_capture != null)
                    {
                        _capture.DataAvailable -= DataAvailable;
                        _capture.RecordingStopped -= RecordingStopped;
                        _capture.Dispose();
                        _capture = null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing capture");
                }

                try
                {
                    _device?.Dispose();
                    _device = null;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing device");
                }
                
                try
                {
                    _waveOut?.Dispose();
                    _waveOut = null;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing waveOut");
                }

                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _cts = null;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error cancelling capture task");
                }

                try
                {
                    _processEvt?.Dispose();
                    _processEvt = null;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing processEvt");
                }
            }
        }

        ~AudioDeviceHandler()
        {
            Dispose(false);
        }
    }
}
