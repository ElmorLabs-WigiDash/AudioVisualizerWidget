﻿using WigiDashWidgetFramework;
using System;
using System.Drawing;
using NAudio.Wave;
using System.Threading;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WigiDashWidgetFramework.WidgetUtility;
using System.Windows.Controls;
using NAudio.Dsp;

namespace AudioVisualizerWidget {
    public partial class WidgetInstance {

        // Allow console for easier debug
        //[DllImport("kernel32.dll", SetLastError = true)]
        //[return: MarshalAs(UnmanagedType.Bool)]
        //static extern bool AllocConsole();

        // Threading Variables
        private bool _pauseDrawing = false;
        private bool _isDrawing = false;
        private readonly Mutex _lockingMutex = new Mutex();

        // Variables
        //private bool DEBUG_FLAG = true;

        private WasapiLoopbackCaptureWorkaround _audioCapture;
        private WaveBuffer _audioBuffer;
        private int _byteLength;

        private Size _visualizerSize;
        private Font _visualizerFont;
        private Color _visualizerBgColor;
        private Color _visualizerBarColor;
        private WidgetTheme _globalTheme;

        private readonly Bitmap _bitmapCurrent;
        
        private float[] _fftBuffer;

        // User Configurable Variables
        public GraphType VisualizerGraphType;
        public int VisualizerDensity;
        public int VisualizerMultiplier;
        public bool UseGlobalTheme;
        public Color UserVisualizerBgColor;
        public Color UserVisualizerBarColor;

        public WidgetInstance(AudioVisualizerWidget parent, WidgetSize widget_size, Guid instance_guid) {
            // Open Console if DEBUG
            //if (DEBUG_FLAG) AllocConsole();

            // Global
            this.parent = parent;
            this.Guid = instance_guid;
            this.WidgetSize = widget_size;

            // Widget Properties
            _visualizerSize = WidgetSize.ToSize();
            _bitmapCurrent = new Bitmap(_visualizerSize.Width, _visualizerSize.Height);

            // Load Settings from Store
            LoadSettings();

            // Hook to update theme
            parent.WidgetManager.GlobalThemeUpdated += UpdateSettings;

            // Audio Capture
            //ScanForDevice();

            // Clear Widget
            ClearWidget();

            Thread task_thread = new Thread(new ThreadStart(TaskThread));
            task_thread.IsBackground = true;
            run_task = true;
            task_thread.Start();
        }

        private volatile bool run_task;

        private void TaskThread() {
            while(run_task) {
                if(_audioCapture == null) {
                    bool result = ScanForDevice();
                    if(result) {
                        ClearWidget();
                        _pauseDrawing = false;
                    }
                }
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Update additional settings dependent on settings from store
        /// </summary>
        public void UpdateSettings()
        {
            if (!UseGlobalTheme)
            {
                _visualizerFont = new Font("Basic Square 7", 32);
                _visualizerBarColor = UserVisualizerBarColor;
                _visualizerBgColor = UserVisualizerBgColor;
            }
            else
            {
                _visualizerFont = _globalTheme.PrimaryFont;
                _visualizerBarColor = _globalTheme.PrimaryFgColor;
                _visualizerBgColor = _globalTheme.PrimaryBgColor;
            }
        }

        /// <summary>
        /// Save settings to store
        /// </summary>
        public void SaveSettings()
        {
            parent.WidgetManager.StoreSetting(this, "useGlobalTheme", UseGlobalTheme.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerGraphType", VisualizerGraphType.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerDensity", VisualizerDensity.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerMultiplier", VisualizerMultiplier.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerBgColor", ColorTranslator.ToHtml(UserVisualizerBgColor));
            parent.WidgetManager.StoreSetting(this, "visualizerBarColor", ColorTranslator.ToHtml(UserVisualizerBarColor));
        }

        /// <summary>
        /// Load settings from store
        /// </summary>
        public void LoadSettings()
        {
            // Variable definitions
            parent.WidgetManager.LoadSetting(this, "useGlobalTheme", out var useGlobalThemeStr);
            parent.WidgetManager.LoadSetting(this, "visualizerGraphType", out var visualizerGraphTypeStr);
            parent.WidgetManager.LoadSetting(this, "visualizerDensity", out var visualizerDensityStr);
            parent.WidgetManager.LoadSetting(this, "visualizerMultiplier", out var visualizerMultiplierStr);
            parent.WidgetManager.LoadSetting(this, "visualizerBgColor", out var visualizerBgColorStr);
            parent.WidgetManager.LoadSetting(this, "visualizerBarColor", out var visualizerBarColorStr);

            // Actually parse and set settings
            try
            {
                _globalTheme = parent.WidgetManager.GlobalWidgetTheme;

                if (!string.IsNullOrEmpty(useGlobalThemeStr)) bool.TryParse(useGlobalThemeStr, out UseGlobalTheme);
                else UseGlobalTheme = false;
                if (!string.IsNullOrEmpty(visualizerGraphTypeStr)) Enum.TryParse(visualizerGraphTypeStr, out VisualizerGraphType);
                else VisualizerGraphType = GraphType.BarGraph;
                if (!string.IsNullOrEmpty(visualizerDensityStr)) int.TryParse(visualizerDensityStr, out VisualizerDensity);
                else VisualizerDensity = 5;
                if (!string.IsNullOrEmpty(visualizerMultiplierStr)) int.TryParse(visualizerMultiplierStr, out VisualizerMultiplier);
                else VisualizerMultiplier = 30;
                if (!string.IsNullOrEmpty(visualizerBgColorStr)) UserVisualizerBgColor = ColorTranslator.FromHtml(visualizerBgColorStr);
                else UserVisualizerBgColor = Color.FromArgb(0, 32, 63);
                if (!string.IsNullOrEmpty(visualizerBarColorStr)) UserVisualizerBarColor = ColorTranslator.FromHtml(visualizerBarColorStr);
                else UserVisualizerBarColor = Color.FromArgb(173, 239, 209);

                UpdateSettings();
                SaveSettings();
            } catch (Exception _)
            {
                //MessageBox.Show(ex.Message);
            }
        }

        /// <summary>
        /// Scans for devices for the wasapi to hook into
        /// </summary>
        /// <returns></returns>
        private bool ScanForDevice()
        {
            _audioCapture = null;

            try
            {
                // Get multimedia assigned audio devices
                MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
                MMDevice mmd = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                // Set audio capture parameters
                _audioCapture = new WasapiLoopbackCaptureWorkaround();
                //_audioCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mmd.AudioClient.MixFormat.SampleRate, 2);
                _audioCapture.DataAvailable += DataAvailable;
                _audioCapture.RecordingStopped += (sender, args) => { RecordingStopped(); };
                _audioCapture.StartRecording();

                return (_audioCapture != null);
            }
            catch (Exception ex)
            {
                HandleCaptureException(ex);
                return false;
            }
        }

        /// <summary>
        /// Handles DataAvailable event
        /// </summary>
        /// <param name="sender">The sender of event</param>
        /// <param name="e">Arguments of event</param>
        private void DataAvailable(object sender, WaveInEventArgs e)
        {
            _audioBuffer = new WaveBuffer(e.Buffer);
            _byteLength = e.BytesRecorded;
            DrawWidget();
        }

        /// <summary>
        /// Handles audio capture exception
        /// </summary>
        /// <param name="ex">Exception</param>
        private void HandleCaptureException(Exception ex)
        {
            _pauseDrawing = true;
            using (Graphics g = Graphics.FromImage(_bitmapCurrent))
            {
                g.Clear(_visualizerBgColor);
                SolidBrush visualizerBrush = new SolidBrush(_visualizerBarColor);
                g.DrawString("Unsupported Format..", _visualizerFont, visualizerBrush, new Point(0, 0));
            }
            UpdateWidget();
            _audioCapture.Dispose();
        }

        /// <summary>
        /// Handles RecordingStopped event
        /// </summary>
        private void RecordingStopped()
        {
            // Draw "No Audio Device" message
            _pauseDrawing = true;
            using (Graphics g = Graphics.FromImage(_bitmapCurrent))
            {
                g.Clear(_visualizerBgColor);
                SolidBrush visualizerBrush = new SolidBrush(_visualizerBarColor);
                g.DrawString("No Audio Device..", _visualizerFont, visualizerBrush, new Point(0, 0));
            }
            UpdateWidget();
            _audioCapture.Dispose();
        }
        
        /// <summary>
        /// Clears widget
        /// </summary>
        public void ClearWidget()
        {
            using (Graphics g = Graphics.FromImage(_bitmapCurrent))
            {
                g.Clear(_visualizerBgColor);
            }

            UpdateWidget();
        }

        /// <summary>
        /// Main drawing method
        /// </summary>
        public void DrawWidget()
        {
            // Check drawing conditions
            if (_lockingMutex.WaitOne(1000) && !_isDrawing && !_pauseDrawing)
            {
                using (Graphics g = Graphics.FromImage(_bitmapCurrent))
                {
                    // Set flag
                    _isDrawing = true;
                    
                    // Check buffer
                    if (_audioBuffer == null)
                    {
                        RecordingStopped();
                        return;
                    }

                    GetValues(_audioBuffer, _byteLength);

                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // Clear board to draw visualizer
                    g.Clear(_visualizerBgColor);

                    switch (VisualizerGraphType)
                    {
                        default:
                        case GraphType.BarGraph:
                            DrawBarGraph(g);
                            break;

                        case GraphType.LineGraph:
                            DrawLineGraph(g);
                            break;
                    }

                    // Flush
                    UpdateWidget();
                }
                _lockingMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Draw wavebuffer to a bar graph
        /// </summary>
        /// <param name="g">Graphics to draw bar to</param>
        /// <returns></returns>
        private void DrawBarGraph(Graphics g)
        {
            int barCount = VisualizerDensity;
            float barWidth = _visualizerSize.Width / (float)barCount;

            if (_fftBuffer.Length <= 0) return;

            float[] heightMap = ResampleAverage(_fftBuffer, barCount);

            for (int i = 0; i < barCount; i++)
            {
                float value = -Math.Abs(heightMap[i]);
                float barHeight = DrawingHeightCalc(value);

                RectangleF bar = VisualizerBar(
                    i * barWidth,
                    _visualizerSize.Height,
                    barWidth,
                    barHeight
                );

                SolidBrush visualizerBrush = new SolidBrush(_visualizerBarColor);
                g.FillRectangle(visualizerBrush, bar);
            }
        }

        /// <summary>
        /// Draw wavebuffer to a line graph
        /// </summary>
        /// <param name="g">Graphics to draw bar to</param>
        /// <returns></returns>
        private void DrawLineGraph(Graphics g)
        {
            int barCount = VisualizerDensity;
            float barWidth = _visualizerSize.Width / (float)barCount;

            if (_fftBuffer.Length <= 0) return;

            float[] heightMap = ResampleAverage(_fftBuffer, barCount);

            List<PointF> pointCoords = new List<PointF>();
            float yOrigin = _visualizerSize.Height - 5;
            pointCoords.Add(new PointF(0, yOrigin));

            for (int i = 0; i < barCount; i++)
            {
                float value = -Math.Abs(heightMap[i]);
                float pointHeight = yOrigin - Math.Abs(DrawingHeightCalc(value));

                PointF point = new PointF((i - 1) * barWidth + (barWidth / 2), pointHeight);
                pointCoords.Add(point);
            }
            pointCoords.Add(new PointF(_visualizerSize.Width, yOrigin));
            
            Pen visualizerPen = new Pen(_visualizerBarColor, 3);
            g.DrawCurve(visualizerPen, pointCoords.ToArray());
        }

        public static float[] ResampleAverage(float[] sample, int targetCount)
        {
            int divisor = sample.Length / targetCount;

            var reducedSamples = new float[sample.Length / divisor];
            int reducedIndex = 0;
            for (int i = 0; i < sample.Length; i += divisor)
            {
                float sum = 0;
                int count = 0;
                for (int j = 0; j < divisor && (i + j) < sample.Length; j++)
                {
                    sum += sample[i + j];
                    count++;
                }

                if (reducedSamples.Length <= reducedIndex) continue;
                reducedSamples[reducedIndex++] = sum / count;
            }

            return reducedSamples;
        }

        /// <summary>
        /// Get current audio source's output
        /// </summary>
        /// <param name="buffer">WaveBuffer to draw graphs from</param>
        /// <returns></returns>
        private void GetValues(WaveBuffer buffer, int byteLength)
        {
            float[] combinedSample = Enumerable
                .Range(0, byteLength / 4)
                .Select(x => BitConverter.ToSingle(buffer, x * 4))
                .ToArray();

            int channelCount = _audioCapture.WaveFormat.Channels;
            float[][] channelSamples = Enumerable.Range(0, channelCount).Select(channelSample => Enumerable
                    .Range(0, combinedSample.Length / 2)
                    .Select(x => combinedSample[channelSample + x * channelCount])
                    .ToArray())
                .ToArray();

            float[] sampleAverage = Enumerable
                .Range(0, combinedSample.Length / channelCount)
                .Select(index => Enumerable
                    .Range(0, channelCount)
                    .Select(x => channelSamples[x][index])
                    .Average())
                .ToArray();

            double logVal = Math.Ceiling(Math.Log(sampleAverage.Length, 2));
            int lenVal = (int)Math.Pow(2, logVal);
            float[] sampleBuffer = new float[lenVal];

            Array.Copy(sampleAverage, sampleBuffer, sampleAverage.Length);
            Complex[] complexSource = sampleBuffer
                .Select(v => new Complex() { X = v })
                .ToArray();

            FastFourierTransform.FFT(false, (int)logVal, complexSource);

            Complex[] halvedSample = complexSource
                .Take(complexSource.Length / 2)
                .ToArray();

            float[] freqDomainSample = halvedSample
                .Select(v => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y))
                .ToArray();
            
            _fftBuffer = freqDomainSample;

            /**int len = buffer.FloatBuffer.Length / 8;

            fftBuffer = new NAudio.Dsp.Complex[len];

            for (int i = 0; i < len; i++)
            {
                fftBuffer[i].Y = 0;
                fftBuffer[i].X = buffer.FloatBuffer[i];
            }

            NAudio.Dsp.FastFourierTransform.FFT(true, visualizerDensity, fftBuffer);**/
        }

        /// <summary>
        /// Calculate visualizer bar height
        /// </summary>
        /// <param name="i">The nth place of bar</param>
        /// <param name="value">The FFT value of frequency</param>
        /// <returns></returns>
        private float DrawingHeightCalc(float value)
        {
            float returnVal = value * VisualizerMultiplier;
            if (returnVal > _visualizerSize.Height) returnVal = _visualizerSize.Height;
            return returnVal;
        }

        /// <summary>
        /// Flushes the buffer bitmap to widget
        /// </summary>
        private void UpdateWidget() {
            WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
            e.WidgetBitmap = _bitmapCurrent;
            e.WaitMax = 1000;
            WidgetUpdated?.Invoke(this, e);
            _isDrawing = false;
        }

        /// <summary>
        /// Define VisualizerBar for negative height
        /// </summary>
        /// <param name="x_in">X coordinate for rectangle</param>
        /// <param name="y_in">Y coordinate for rectangle</param>
        /// <param name="w_in">Width of rectangle</param>
        /// <param name="h_in">Height of rectangle</param>
        /// <returns></returns>
        private RectangleF VisualizerBar(float x_in, float y_in, float w_in, float h_in)
        {
            float x, y, w, h;

            x = x_in;
            y = y_in;
            w = w_in;
            h = h_in;

            if (h_in < 0)
            {
                h = -h_in;
                y = y_in + h_in;
            }

            return new RectangleF(x, y, w, h);
        }

        /// <summary>
        /// Widget functionality methods
        /// </summary>
        public void RequestUpdate()
        {
            DrawWidget();
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            // Single Click Handler
            if (click_type == ClickType.Single)
            {

            }
            // Double Click Hander
            else if (click_type == ClickType.Double)
            {

            }
            // Long Click Hander
            else if (click_type == ClickType.Long)
            {

            }
        }

        public void Dispose() {
            _pauseDrawing = true;
            run_task = false;
            if(_audioCapture != null) {
                _audioCapture.StopRecording();
                _audioCapture.Dispose();
            }
            Thread.Sleep(50);
            _bitmapCurrent.Dispose();
        }

        public void EnterSleep()
        {
            _pauseDrawing = true;
        }

        public void ExitSleep()
        {
            _pauseDrawing = false;
            UpdateWidget();
        }

        public UserControl GetSettingsControl()
        {
            return new SettingsControl(this);
        }
    }

    /// <summary>
    /// Workaround for wasapi
    /// </summary>
    class WasapiLoopbackCaptureWorkaround : WasapiCapture
    {
        public WasapiLoopbackCaptureWorkaround() :
            this(GetDefaultLoopbackCaptureDevice())
        {
        }

        public WasapiLoopbackCaptureWorkaround(MMDevice captureDevice) :
            base(captureDevice)
        {
        }

        public static MMDevice GetDefaultLoopbackCaptureDevice()
        {
            MMDeviceEnumerator devices = new MMDeviceEnumerator();
            return devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        public override WaveFormat WaveFormat
        {
            get { return base.WaveFormat; }
            set { base.WaveFormat = value; }
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        {
            return AudioClientStreamFlags.Loopback;
        }

        protected new void Dispose()
        {
            StopRecording();
            base.Dispose();
        }
    }
}

