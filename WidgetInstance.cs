using FrontierWidgetFramework;
using System;
using System.Drawing;
using NAudio.Wave;
using System.Threading;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FrontierWidgetFramework.WidgetUtility;
using System.Windows.Controls;

namespace AudioVisualizerWidget {
    public partial class WidgetInstance {

        // Allow console for easier debug
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        // Threading Variables
        private bool pause_drawing = false;
        private bool is_drawing = false;

        // Variables
        private bool DEBUG_FLAG = true;

        private WasapiLoopbackCaptureWorkaround audioCapture;
        private WaveBuffer audioBuffer;

        private Size visualizerSize;
        private Font visualizerFont;
        private SolidBrush visualizerBrush;

        private int barCount;
        private float barWidth;
        private float[] barValues;
        private Bitmap BitmapCurrent;

        // User Configurable Variables
        public int visualizerDensity;
        public int visualizerMultiplier;
        public Color visualizerBgColor;
        public Color visualizerBarColor;

        public WidgetInstance(AudioVisualizerWidget parent, WidgetSize widget_size, Guid instance_guid) {
            // Open Console if DEBUG
            if (DEBUG_FLAG) AllocConsole();

            // Global
            this.parent = parent;
            this.Guid = instance_guid;
            this.WidgetSize = widget_size;

            // Widget Properties
            visualizerSize = this.WidgetSize.ToSize();
            BitmapCurrent = new Bitmap(visualizerSize.Width, visualizerSize.Height);

            // UI
            visualizerFont = new Font("Basic Square 7", 32);

            // Load Settings from Store
            LoadSettings();

            // Audio Capture
            ScanForDevice();

            // Clear Widget
            ClearWidget();
        }

        /// <summary>
        /// Update additional settings dependent on settings from store
        /// </summary>
        public void UpdateSettings()
        {
            // Set Brush
            visualizerBrush = new SolidBrush(visualizerBarColor);

            // Set visualizer variables
            barCount = (int)((Math.Pow(2, visualizerDensity) / 2) + 1);
            barValues = new float[barCount];
            barWidth = visualizerSize.Width / ((float)Math.Pow(2, visualizerDensity) / 2);
        }

        /// <summary>
        /// Save settings to store
        /// </summary>
        public void SaveSettings()
        {
            parent.WidgetManager.StoreSetting(this, "visualizerDensity", visualizerDensity.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerMultiplier", visualizerMultiplier.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerBgColor", ColorTranslator.ToHtml(visualizerBgColor));
            parent.WidgetManager.StoreSetting(this, "visualizerBarColor", ColorTranslator.ToHtml(visualizerBarColor));
        }

        /// <summary>
        /// Load settings from store
        /// </summary>
        public void LoadSettings()
        {
            // Variable definitions
            string visualizerDensityStr;
            string visualizerMultiplierStr;
            string visualizerBgColorStr;
            string visualizerBarColorStr;

            parent.WidgetManager.LoadSetting(this, "visualizerDensity", out visualizerDensityStr);
            parent.WidgetManager.LoadSetting(this, "visualizerMultiplier", out visualizerMultiplierStr);
            parent.WidgetManager.LoadSetting(this, "visualizerBgColor", out visualizerBgColorStr);
            parent.WidgetManager.LoadSetting(this, "visualizerBarColor", out visualizerBarColorStr);

            // Actually parse and set settings
            try
            {
                if (!string.IsNullOrEmpty(visualizerDensityStr)) int.TryParse(visualizerDensityStr, out visualizerDensity);
                else visualizerDensity = 5;
                if (!string.IsNullOrEmpty(visualizerMultiplierStr)) int.TryParse(visualizerMultiplierStr, out visualizerMultiplier);
                else visualizerMultiplier = 30;
                if (!string.IsNullOrEmpty(visualizerBgColorStr)) visualizerBgColor = ColorTranslator.FromHtml(visualizerBgColorStr);
                else visualizerBgColor = Color.FromArgb(0, 32, 63);
                if (!string.IsNullOrEmpty(visualizerBarColorStr)) visualizerBarColor = ColorTranslator.FromHtml(visualizerBarColorStr);
                else visualizerBarColor = Color.FromArgb(173, 239, 209);

                UpdateSettings();
                SaveSettings();
            } catch (Exception ex)
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
            audioCapture = null;

            try
            {
                // Get multimedia assigned audio devices
                MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
                MMDevice mmd = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                // Set audio capture parameters
                audioCapture = new WasapiLoopbackCaptureWorkaround();
                audioCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mmd.AudioClient.MixFormat.SampleRate, 2);
                audioCapture.DataAvailable += DataAvailable;
                audioCapture.RecordingStopped += (sender, args) => { RecordingStopped(); };
                audioCapture.StartRecording();

                return (audioCapture != null);
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
            audioBuffer = new WaveBuffer(e.Buffer);
            DrawWidget(audioBuffer);
        }

        /// <summary>
        /// Handles audio capture exception
        /// </summary>
        /// <param name="ex">Exception</param>
        private void HandleCaptureException(Exception ex)
        {
            pause_drawing = true;
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.Clear(visualizerBgColor);
                g.DrawString("Unsupported Format..", visualizerFont, visualizerBrush, new Point(0, 0));
            }
            UpdateWidget();
            audioCapture.Dispose();
        }

        /// <summary>
        /// Handles RecordingStopped event
        /// </summary>
        private void RecordingStopped()
        {
            // Draw "No Audio Device" message
            pause_drawing = true;
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.Clear(visualizerBgColor);
                g.DrawString("No Audio Device..", visualizerFont, visualizerBrush, new Point(0, 0));
            }
            UpdateWidget();
            audioCapture.Dispose();

            // Scan for changes in audio device
            while (true)
            {
                bool result = ScanForDevice();
                if (result)
                {
                    ClearWidget();
                    pause_drawing = false;
                    break;
                }
                Thread.Sleep(1000);
            }
        }
        
        /// <summary>
        /// Clears widget
        /// </summary>
        public void ClearWidget()
        {
            using (Graphics g = Graphics.FromImage(BitmapCurrent))
            {
                g.Clear(visualizerBgColor);
            }

            UpdateWidget();
        }

        /// <summary>
        /// Main drawing method
        /// </summary>
        /// <param name="buffer">WaveBuffer to draw graphs from</param>
        public void DrawWidget(WaveBuffer buffer)
        {
            // Check drawing conditions
            if (!is_drawing && !pause_drawing)
            {
                using (Graphics g = Graphics.FromImage(BitmapCurrent))
                {
                    // Set flag
                    is_drawing = true;
                    
                    // Check buffer
                    if (buffer == null)
                    {
                        RecordingStopped();
                        return;
                    }


                    // FFT
                    int len = buffer.FloatBuffer.Length / 8;

                    NAudio.Dsp.Complex[] values = new NAudio.Dsp.Complex[len];
                    for (int i = 0; i < len; i++)
                    {
                        values[i].Y = 0;
                        values[i].X = buffer.FloatBuffer[i];
                    }
                    NAudio.Dsp.FastFourierTransform.FFT(true, visualizerDensity, values);

                    // Clear board to draw visualizer
                    g.Clear(visualizerBgColor);
                    
                    // For each FFT values, draw a bar
                    for (int i = 1; i < barCount; i++)
                    {
                        float value = -(Math.Abs(values[i].X));
                        float barHeight = BarHeightCalc(i, value);

                        RectangleF visualizerBar = VisualizerBar(
                            (i - 1) * barWidth,
                            visualizerSize.Height,
                            barWidth,
                            barHeight
                        );

                        g.FillRectangle(visualizerBrush, visualizerBar);
                    }

                    // Flush
                    UpdateWidget();
                }
            }
        }

        /// <summary>
        /// Calculate visualizer bar height
        /// </summary>
        /// <param name="i">The nth place of bar</param>
        /// <param name="value">The FFT value of frequency</param>
        /// <returns></returns>
        private float BarHeightCalc(int i, float value)
        {
            if (Math.Abs(barValues[i]) > Math.Abs(value))
            {
                // Decay
                barValues[i] = -(value * barValues[i]) * (95f / 100f);
            } else
            {
                // Set as normally would
                barValues[i] = value;
            }

            float returnVal = barValues[i] * visualizerMultiplier * visualizerSize.Height;
            return returnVal;
        }

        /// <summary>
        /// Flushes the buffer bitmap to widget
        /// </summary>
        private void UpdateWidget() {
            WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs();
            e.WidgetBitmap = BitmapCurrent;
            e.WaitMax = 1000;
            WidgetUpdated?.Invoke(this, e);
            is_drawing = false;
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
            DrawWidget(audioBuffer);
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

        public void Dispose()
        {
            pause_drawing = true;
            Thread.Sleep(50);
            BitmapCurrent.Dispose();
        }

        public void EnterSleep()
        {
            pause_drawing = true;
        }

        public void ExitSleep()
        {
            pause_drawing = false;
            UpdateWidget();
        }

        public UserControl GetSettingsControl()
        {
            return new SettingsUserControl(this);
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
    }
}

