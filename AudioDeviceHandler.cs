﻿using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AudioVisualizerWidget
{
    class AudioDeviceHandler : IDisposable
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private CancellationToken _token;
        private AutoResetEvent _processEvt = new AutoResetEvent(false);
        private WasapiCapture _capture;
        private MMDevice _device;
        private WaveFormat _waveFormat;
        private SampleReader _reader;
        private readonly double[] _input;
        private readonly double[] _inputBack;
        private double[] _currentBuffer;

        public int SamplesPerSecond { get; }
        public int BufferSize { get; }

        public double[] Samples => _inputBack;
        public double[] CurrentBuffer => _currentBuffer;

        public event EventHandler DataReceived;

        public AudioDeviceHandler(MMDevice device)
        {
            Console.WriteLine($"AudioDeviceHandler: Device: {device.FriendlyName}");
            _token = _cts.Token;
            _device = device;

            _waveFormat = device.AudioClient.MixFormat.AsStandardWaveFormat();

            SamplesPerSecond = _waveFormat.SampleRate;

            Console.WriteLine($"AudioDeviceHandler: Sample rate: {SamplesPerSecond}");

            BufferSize = SamplesPerSecond * 10;

            Console.WriteLine($"AudioDeviceHandler: Buffer size: {BufferSize}");

            _input = new double[BufferSize];
            _inputBack = new double[BufferSize];
            _currentBuffer = new double[(int)(SamplesPerSecond * 0.1)];

            Console.WriteLine("AudioDeviceHandler: Hooking into audio device...");

            var capture = new WasapiLoopbackCapture(device);
            capture.DataAvailable += DataAvailable;
            capture.RecordingStopped += RecordingStopped;

            _capture = capture;

            _reader = new SampleReader(_waveFormat);

            // Play silence to initialize the audio device
            Console.WriteLine("AudioDeviceHandler: Playing Silence...");
            var silence = new SilenceProvider(_waveFormat).ToSampleProvider();
            var wo = new WaveOutEvent();
            wo.Init(silence);
            wo.Play();

            Console.WriteLine("AudioDeviceHandler: Starting audio capture...");
            _ = Task.Run(ProcessData, _cts.Token);
        }

        public void Start()
        {
            if (_capture.CaptureState == CaptureState.Stopped)
            {
                Console.WriteLine("AudioDeviceHandler: Starting Capture...");
                _capture.StartRecording();
            }
        }

        public void Stop()
        {
            if (_capture.CaptureState == CaptureState.Capturing)
            {
                Console.WriteLine("AudioDeviceHandler: Stopping Capture...");
                _capture.StopRecording();
            }
        }

        private void ProcessData()
        {
            while (!_token.IsCancellationRequested)
            {
                if (_processEvt.WaitOne(100))
                {
                    lock (_input)
                    {
                        Array.Copy(_input, _inputBack, _input.Length);
                        Array.Copy(_input, _input.Length - _currentBuffer.Length - 1, _currentBuffer, 0, _currentBuffer.Length);
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
                _processEvt.Set();
            }
        }

        public void Dispose()
        {
            if (_capture.CaptureState == CaptureState.Stopped)
            {
                _capture?.Dispose();
                _device?.Dispose();
            }
            else
            {
                Stop();
            }
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
        }
    }
}
