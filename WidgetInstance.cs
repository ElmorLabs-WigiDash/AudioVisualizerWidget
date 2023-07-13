using WigiDashWidgetFramework;
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
using System.Threading.Tasks;
using ScottPlot;
using MathNet.Numerics.Interpolation;
using System.IO;

namespace AudioVisualizerWidget
{
    public partial class WidgetInstance
    {

        // Allow console for easier debug
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        // Threading Variables
        private bool _pauseDrawing = false;
        private readonly Mutex _bitmapLock = new Mutex();
        private const int mutex_timeout = 100;
        private volatile bool run_task;

        // Variables
        //private bool DEBUG_FLAG = true;

        private Size _visualizerSize;
        private Font _visualizerFont;
        private Color _visualizerBgColor;
        private Color _visualizerBarColor;
        private WidgetTheme _globalTheme;

        private readonly Bitmap _bitmapCurrent;

        // User Configurable Variables
        public GraphType VisualizerGraphType;
        public int VisualizerDensity;
        public bool VisualizerNormalize;
        public bool VisualizerShowGrid;
        public bool VisualizerShowAxis;
        public bool UseGlobalTheme;
        public Color UserVisualizerBgColor;
        public Color UserVisualizerBarColor;

        private readonly AudioDeviceSource _audioDeviceSource = new AudioDeviceSource();
        private AudioDeviceHandler _audioDeviceHandler;
        private AudioDataAnalyzer _audioDataAnalyzer;

        private Dictionary<int, double> _frequencyDataSeries = new Dictionary<int, double>();

        public WidgetInstance(AudioVisualizerWidget parent, WidgetSize widget_size, Guid instance_guid)
        {
            // Open Console if DEBUG
#if DEBUG
            AllocConsole();
#endif

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


            // Get default device
            Console.WriteLine("Initializer: Finding default device...");
            string defaultDeviceId = FindDefaultDevice();

            if (defaultDeviceId == string.Empty)
            {
                Console.WriteLine("Initializer: No supported devices found!");
                ClearWidget("No supported devices!");
                return;
            }

            // Clear Widget
            ClearWidget();

            // Hook to default device
            Console.WriteLine("Initializer: Found default device: " + defaultDeviceId);
            bool hookResult = HandleInputDeviceChange(defaultDeviceId);

            if (!hookResult)
            {
                ClearWidget("Could not hook into audio device!");
                return;
            }

            // Start Drawing every 100ms
            run_task = true;
            Task.Run(() =>
            {
                while (run_task)
                {
                    if (!_pauseDrawing)
                    {
                        DrawWidget();
                    }

                    // Limit to 10 FPS
                    Thread.Sleep(100);
                }
            });
        }

        /// <summary>
        /// Update additional settings dependent on settings from store
        /// </summary>
        public void UpdateSettings()
        {
            if (UseGlobalTheme || parent.WidgetManager.PreferGlobalTheme)
            {
                _visualizerFont = _globalTheme.PrimaryFont;
                _visualizerBarColor = _globalTheme.PrimaryFgColor;
                _visualizerBgColor = _globalTheme.PrimaryBgColor;
            }
            else
            {
                _visualizerFont = new Font("Basic Square 7", 32);
                _visualizerBarColor = UserVisualizerBarColor;
                _visualizerBgColor = UserVisualizerBgColor;
            }
        }

        /// <summary>
        /// Save settings to store
        /// </summary>
        public void SaveSettings()
        {
            parent.WidgetManager.StoreSetting(this, "useGlobalTheme", UseGlobalTheme.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerGraphType", VisualizerGraphType.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerShowGrid", VisualizerShowGrid.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerShowAxis", VisualizerShowAxis.ToString());
            parent.WidgetManager.StoreSetting(this, "visualizerDensity", VisualizerDensity.ToString());
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
            parent.WidgetManager.LoadSetting(this, "visualizerShowGrid", out var visualizerShowGridStr);
            parent.WidgetManager.LoadSetting(this, "visualizerShowAxis", out var visualizerShowAxisStr);
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
                if (!string.IsNullOrEmpty(visualizerShowGridStr)) bool.TryParse(visualizerShowGridStr, out VisualizerShowGrid);
                else VisualizerShowGrid = false;
                if (!string.IsNullOrEmpty(visualizerShowAxisStr)) bool.TryParse(visualizerShowAxisStr, out VisualizerShowAxis);
                else VisualizerShowAxis = false;
                if (!string.IsNullOrEmpty(visualizerDensityStr)) int.TryParse(visualizerDensityStr, out VisualizerDensity);
                else VisualizerDensity = 50;
                if (!string.IsNullOrEmpty(visualizerBgColorStr)) UserVisualizerBgColor = ColorTranslator.FromHtml(visualizerBgColorStr);
                else UserVisualizerBgColor = Color.FromArgb(0, 32, 63);
                if (!string.IsNullOrEmpty(visualizerBarColorStr)) UserVisualizerBarColor = ColorTranslator.FromHtml(visualizerBarColorStr);
                else UserVisualizerBarColor = Color.FromArgb(173, 239, 209);

                UpdateSettings();
                SaveSettings();
            }
            catch (Exception _)
            {
                //MessageBox.Show(ex.Message);
            }
        }

        private string FindDefaultDevice()
        {
            string deviceId = _audioDeviceSource.DefaultDevice ?? _audioDeviceSource.Devices[0]?.ID ?? string.Empty;

            return deviceId;
        }

        private bool HandleInputDeviceChange(string newId)
        {
            // Null check
            if (string.IsNullOrEmpty(newId))
            {
                Console.WriteLine("HandleInputDeviceChange: Invalid device ID!");
                return false;
            }

            // Dispose any previous handlers
            Console.WriteLine("HandleInputDeviceChange: Disposing previous handler...");
            _audioDeviceHandler?.Dispose();
            _audioDeviceHandler = null;

            // Dispose any previous analyzers
            if (_audioDataAnalyzer != null)
            {
                Console.WriteLine("HandleInputDeviceChange: Disposing previous analyzer...");
                _audioDataAnalyzer.Update -= Update;
                _audioDataAnalyzer = null;
            }

            try
            {
                // Create new handler and analyzer
                Console.WriteLine("HandleInputDeviceChange: Creating new handler...");
                _audioDeviceHandler = _audioDeviceSource.CreateHandler(newId);
                _audioDataAnalyzer = new AudioDataAnalyzer(_audioDeviceHandler);

                // Hook into analyzer
                Console.WriteLine("HandleInputDeviceChange: Hooking into analyzer...");
                _audioDataAnalyzer.Update += Update;

                // Initialize data storage
                InitDataStorage(_audioDataAnalyzer, _audioDeviceHandler);

                // Start handler
                Console.WriteLine("HandleInputDeviceChange: Starting handler...");
                _audioDeviceHandler.Start();
            } catch
            {
                return false;
            }

            return true;
        }

        private void InitDataStorage(AudioDataAnalyzer analyzer, AudioDeviceHandler handler)
        {
            Console.WriteLine("InitDataStorage: Initializing data storage...");
            _frequencyDataSeries = new Dictionary<int, double>(analyzer.FftDataPoints);
            _frequencyDataSeries.Append(analyzer.FftIndices, analyzer.DbValues);
        }

        // For event hook
        private void Update(object sender, EventArgs e)
        {
            Update((AudioDataAnalyzer)sender);
        }

        private void Update(AudioDataAnalyzer analyzer)
        {
            lock (_frequencyDataSeries)
            {
                _frequencyDataSeries.Clear();
                _frequencyDataSeries.Append(analyzer.FftIndices, analyzer.DbValues);
            }
        }

        /// <summary>
        /// Clears widget
        /// </summary>
        public void ClearWidget(string status = "")
        {
            if (_bitmapLock.WaitOne(mutex_timeout))
            {
                using (Graphics g = Graphics.FromImage(_bitmapCurrent))
                {
                    g.Clear(_visualizerBgColor);

                    if (status != "")
                    {
                        RectangleF layoutRectangle =
                            new RectangleF(0, 0, WidgetSize.ToSize().Width, WidgetSize.ToSize().Height);
                        g.DrawString(status, new Font("Lucida Console", 16, FontStyle.Bold), new SolidBrush(_visualizerBarColor), layoutRectangle);
                    }
                }

                _bitmapLock.ReleaseMutex();
            }
            UpdateWidget();
        }

        /// <summary>
        /// Main drawing method
        /// </summary>
        public void DrawWidget()
        {
            // Don't draw if no data
            if (_bitmapCurrent == null || _frequencyDataSeries.Count <= 0)
            {
                ClearWidget();
                return;
            }

            // Check drawing conditions and frequency data values
            if (!_pauseDrawing)
            {
                if (_bitmapLock.WaitOne(mutex_timeout))
                {
                    using (Graphics g = Graphics.FromImage(_bitmapCurrent))
                    {
                        // Set smoothing mode
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                        // Clear board to draw visualizer
                        g.Clear(_visualizerBgColor);
                    }

                    lock (_frequencyDataSeries)
                    {
                        // If _frequencyDataSeries has Infinity or NaN values, set them to 0
                        foreach (var key in _frequencyDataSeries.Keys.ToList())
                        {
                            if (double.IsInfinity(_frequencyDataSeries[key]) || double.IsNaN(_frequencyDataSeries[key]))
                            {
                                _frequencyDataSeries[key] = 0;
                            }
                        }

                        // Draw graph in log10 scale between 20Hz and 25kHz. Y axis is from -200 to 0.
                        var plt = new Plot(WidgetSize.ToSize().Width, WidgetSize.ToSize().Height);

                        var xAxisData = Tools.Log10(_frequencyDataSeries.Keys.Select(i => (double)i).ToArray());
                        var yAxisData = _frequencyDataSeries.Values.Select(d => d + 200 > 190 ? 0 : d + 200).ToArray();

                        var plotData = GetInterpolatedData(xAxisData, yAxisData, VisualizerDensity);

                        switch (VisualizerGraphType)
                        {
                            default:
                            case GraphType.BarGraph:
                                var bars = plt.AddBar(plotData.Item2, _visualizerBarColor);
                                bars.BorderLineWidth = 0;
                                bars.BarWidth = 1.01;
                                break;

                            case GraphType.LineGraph:
                                plt.AddScatter(plotData.Item1, plotData.Item2, _visualizerBarColor, markerShape: MarkerShape.none, lineWidth: 2);
                                break;
                        }

                        plt.Style(dataBackground: _visualizerBgColor, figureBackground: _visualizerBgColor);

                        plt.SetAxisLimitsY(0, 200);

                        if (VisualizerNormalize)
                        {
                            plt.AxisAuto(0, 0);
                        } else
                        {
                            plt.AxisAutoX(0);
                        }

                        plt.XAxis.Color(_visualizerBarColor);
                        plt.XAxis2.Color(_visualizerBarColor);
                        plt.YAxis.Color(_visualizerBarColor);
                        plt.YAxis2.Color(_visualizerBarColor);
                        plt.Grid(VisualizerShowGrid);

                        plt.Frameless(!VisualizerShowAxis);

                        plt.Render(_bitmapCurrent);
                    }
                    
                    // Release bitmap lock
                    _bitmapLock.ReleaseMutex();
                }

                // Flush
                UpdateWidget();
            }
        }

        private (double[], double[]) GetInterpolatedData(double[] xData, double[] yData, int numPoints)
        {
            // Transform x-axis data to logarithmic scale
            var logData = new double[xData.Length];
            for (int i = 0; i < xData.Length; i++)
            {
                logData[i] = Math.Log(xData[i], 10);
                // add check to replace negative or zero data with small positive value
                if (logData[i] <= 0)
                {
                    logData[i] = double.Epsilon;
                }
            }

            // Perform cubic spline interpolation
            var interpolator = CubicSpline.InterpolateNaturalSorted(logData, yData);
            var xInterp = new double[numPoints];
            var yInterp = new double[numPoints];
            var step = (logData.Last() - logData.First()) / (numPoints - 1);

            for (int i = 0; i < numPoints; i++)
            {
                var x = logData.First() + i * step;

                // add check to replace negative or zero data with small positive value
                if (x <= 0)
                {
                    x = double.Epsilon;
                }

                xInterp[i] = Math.Pow(10, x);
                yInterp[i] = interpolator.Interpolate(x);

                // add check to replace NaN data with zero
                if (double.IsNaN(yInterp[i]))
                {
                    yInterp[i] = 0;
                }
            }

            return (xInterp, yInterp);
        }


        /// <summary>
        /// Flushes the buffer bitmap to widget
        /// </summary>
        private void UpdateWidget()
        {
            if (_bitmapLock.WaitOne(mutex_timeout))
            {
                if (_bitmapCurrent == null)
                {
                    _bitmapLock.ReleaseMutex();
                    return;
                }

                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs
                {
                    WidgetBitmap = _bitmapCurrent,
                    WaitMax = 1000
                };

                WidgetUpdated?.Invoke(this, e);

                _bitmapLock.ReleaseMutex();
            }
        }

        /// <summary>
        /// Widget functionality methods
        /// </summary>
        public void RequestUpdate()
        {
            UpdateWidget();
            //DrawWidget();
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

        public void EnterSleep()
        {
            _pauseDrawing = true;
            _audioDeviceHandler?.Stop();
        }

        public void ExitSleep()
        {
            _pauseDrawing = false;
            _audioDeviceHandler?.Start();
        }

        public void Dispose()
        {
            run_task = false;
            _pauseDrawing = true;
        }

        public UserControl GetSettingsControl()
        {
            return new SettingsControl(this);
        }
    }
}

