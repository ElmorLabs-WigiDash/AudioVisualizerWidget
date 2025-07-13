using WigiDashWidgetFramework;
using System;
using System.Drawing;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WigiDashWidgetFramework.WidgetUtility;
using System.Windows.Controls;
using System.Threading.Tasks;
using ScottPlot;
using MathNet.Numerics.Interpolation;
using NLog;

namespace AudioVisualizerWidget
{
    public partial class WidgetInstance
    {
        // Allow console for easier debug
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        // Threading Variables
        private volatile bool _pauseDrawing = false;
        private Mutex _bitmapLock = new Mutex();
        private const int mutex_timeout = 1000;
        private volatile bool _runTask;
        private readonly object _frequencyDataLock = new object();
        private readonly object _deviceLock = new object();

        // Variables
        //private bool DEBUG_FLAG = true;

        private Size _visualizerSize;
        private Font _visualizerFont;
        private System.Drawing.Color _visualizerBgColor;
        private System.Drawing.Color _visualizerBarColor;
        private WidgetTheme _globalTheme;

        private Bitmap _bitmapCurrent;

        // User Configurable Variables
        public GraphType VisualizerGraphType;
        public int VisualizerDensity;
        public bool VisualizerNormalize;
        public bool VisualizerShowGrid;
        public bool VisualizerShowAxis;
        public bool UseGlobalTheme;
        public System.Drawing.Color UserVisualizerBgColor;
        public System.Drawing.Color UserVisualizerBarColor;
        public bool FollowWindowsDefaultDevice;

        private bool _noDevices = true;
        public string PreferredDeviceID = string.Empty;
        public string PreferredDeviceName = string.Empty;
        public string SelectedDeviceID = string.Empty;
        private string _activeDeviceID = string.Empty;
        public AudioDeviceSource AudioDeviceSource;
        private AudioDeviceHandler _audioDeviceHandler;
        private AudioDataAnalyzer _audioDataAnalyzer;

        private Dictionary<int, double> _frequencyDataSeries = new Dictionary<int, double>();

        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        public WidgetInstance(AudioVisualizerWidgetServer parent, WidgetSize widget_size, Guid instance_guid)
        {
            // Global
            this.parent = parent;
            this.Guid = instance_guid;
            this.WidgetSize = widget_size;

            // Widget Properties
            _visualizerSize = WidgetSize.ToSize();
            _bitmapCurrent = new Bitmap(_visualizerSize.Width, _visualizerSize.Height);

            // Create audio device source
            AudioDeviceSource = new AudioDeviceSource();
            AudioDeviceSource.DefaultDeviceChanged += AudioDeviceSource_DefaultDeviceChanged;
            AudioDeviceSource.DevicesChanged += AudioDeviceSource_DevicesChanged;
            AudioDeviceSource.DeviceFormatChanged += AudioDeviceSource_FormatChanged;

            // Load Settings from Store
            LoadSettings();

            // Hook to update theme
            parent.WidgetManager.GlobalThemeUpdated += UpdateSettings;

            Init();

            // Start Drawing every 100ms
            _runTask = true;
            Task.Run(() =>
            {
                try
                {
                    while (_runTask)
                    {
                        if (!_pauseDrawing) DrawWidget();

                        // Limit to 10 FPS
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error in widget drawing task");
                }
            });
        }

        private void AudioDeviceSource_DefaultDeviceChanged(object sender, EventArgs e)
        {
            // Auto-switch to the new default device if option is enabled
            if (FollowWindowsDefaultDevice)
            {
                string defaultDeviceId = AudioDeviceSource.ActivePlaybackDevice;
                Logger.Debug($"Default device changed, auto-switching to: {defaultDeviceId}");
                
                if (!string.IsNullOrEmpty(defaultDeviceId) && defaultDeviceId != _activeDeviceID)
                {
                    HandleInputDeviceChange(defaultDeviceId);
                }
            }
        }

        private void AudioDeviceSource_DevicesChanged(object sender, EventArgs e)
        {
            // Check if the current device is still available
            if (!string.IsNullOrEmpty(_activeDeviceID))
            {
                var deviceStillExists = AudioDeviceSource.Devices.Any(d => d.ID == _activeDeviceID);
                
                if (!deviceStillExists)
                {
                    Logger.Debug($"Current device {_activeDeviceID} is no longer available");
                    
                    // If following Windows default, switch to that
                    if (FollowWindowsDefaultDevice)
                    {
                        string defaultDeviceId = AudioDeviceSource.ActivePlaybackDevice;
                        if (!string.IsNullOrEmpty(defaultDeviceId))
                        {
                            Logger.Debug($"Auto-switching to default device: {defaultDeviceId}");
                            HandleInputDeviceChange(defaultDeviceId);
                        }
                    }
                    // Otherwise, if a preferred device is set and available, use that
                    else if (!string.IsNullOrEmpty(PreferredDeviceID))
                    {
                        var preferredDeviceExists = AudioDeviceSource.Devices.Any(d => d.ID == PreferredDeviceID);
                        if (preferredDeviceExists)
                        {
                            Logger.Debug($"Switching back to preferred device: {PreferredDeviceID}");
                            HandleInputDeviceChange(PreferredDeviceID);
                        }
                    }
                }
            }
        }

        private void AudioDeviceSource_FormatChanged(object sender, string deviceId)
        {
            // Check if this is our active device
            if (!string.IsNullOrEmpty(_activeDeviceID) && deviceId == _activeDeviceID)
            {
                Logger.Debug($"Format changed for active device: {deviceId}, reinitializing handler");
                
                // Reinitialize the audio device handler
                bool wasDrawing = !_pauseDrawing;
                
                // Pause drawing while we reset the handler
                _pauseDrawing = true;
                
                try 
                {
                    // Create a new handler for the same device ID
                    HandleInputDeviceChange(deviceId);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error handling format change");
                    ClearWidget("Error: Format change failed");
                }
                finally
                {
                    // Resume drawing if it was active before
                    _pauseDrawing = !wasDrawing;
                }
            }
        }

        private void Init()
        {
            try
            {
                string deviceIdToUse = string.Empty;

                // Determine which device to use
                if (FollowWindowsDefaultDevice)
                {
                    // Use current Windows default
                    deviceIdToUse = AudioDeviceSource.ActivePlaybackDevice;
                    Logger.Debug($"Follow Windows default enabled, using device: {deviceIdToUse}");
                }
                else if (!string.IsNullOrEmpty(SelectedDeviceID))
                {
                    // Use currently selected device
                    deviceIdToUse = SelectedDeviceID;
                    Logger.Debug($"Using selected device: {deviceIdToUse}");
                }
                else
                {
                    // Get preferred or default device
                    deviceIdToUse = FindDefaultDevice();
                    Logger.Debug($"Using preferred/default device: {deviceIdToUse}");
                }

                if (string.IsNullOrEmpty(deviceIdToUse))
                {
                    Logger.Debug("Initializer: No supported device found");
                    _noDevices = true;
                    ClearWidget("No supported device found");
                    return;
                }
                else
                {
                    // Clear Widget
                    ClearWidget();

                    // Hook to default device
                    Logger.Debug($"Initializer: Found device: {deviceIdToUse}");
                    bool hookResult = HandleInputDeviceChange(deviceIdToUse);

                    if (!hookResult)
                    {
                        _activeDeviceID = string.Empty;
                        SelectedDeviceID = string.Empty;
                        ClearWidget("Could not hook into audio device!");
                        return;
                    }
                    else
                    {
                        _noDevices = false;
                    }
                }
                
            }
            catch (Exception ex)
            {
                _activeDeviceID = string.Empty;
                SelectedDeviceID = string.Empty;
                Logger.Error(ex, "Initializer: Hooking in failed");
                ClearWidget("No supported devices!");
                return;
            }
        }

        /// <summary>
        /// Update additional settings dependent on settings from store
        /// </summary>
        public void UpdateSettings()
        {
            if (UseGlobalTheme)
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
            try
            {
                parent.WidgetManager.StoreSetting(this, "DeviceID", PreferredDeviceID);
                parent.WidgetManager.StoreSetting(this, "DeviceName", PreferredDeviceName);
                parent.WidgetManager.StoreSetting(this, "useGlobalTheme", UseGlobalTheme.ToString());
                parent.WidgetManager.StoreSetting(this, "visualizerGraphType", VisualizerGraphType.ToString());
                parent.WidgetManager.StoreSetting(this, "visualizerShowGrid", VisualizerShowGrid.ToString());
                parent.WidgetManager.StoreSetting(this, "visualizerShowAxis", VisualizerShowAxis.ToString());
                parent.WidgetManager.StoreSetting(this, "visualizerDensity", VisualizerDensity.ToString());
                parent.WidgetManager.StoreSetting(this, "visualizerBgColor", ColorTranslator.ToHtml(UserVisualizerBgColor));
                parent.WidgetManager.StoreSetting(this, "visualizerBarColor", ColorTranslator.ToHtml(UserVisualizerBarColor));
                parent.WidgetManager.StoreSetting(this, "followWindowsDefault", FollowWindowsDefaultDevice.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving settings");
            }
        }

        /// <summary>
        /// Load settings from store
        /// </summary>
        public void LoadSettings()
        {
            // Variable definitions
            parent.WidgetManager.LoadSetting(this, "DeviceID", out PreferredDeviceID);
            parent.WidgetManager.LoadSetting(this, "DeviceName", out PreferredDeviceName);
            parent.WidgetManager.LoadSetting(this, "useGlobalTheme", out var useGlobalThemeStr);
            parent.WidgetManager.LoadSetting(this, "visualizerGraphType", out var visualizerGraphTypeStr);
            parent.WidgetManager.LoadSetting(this, "visualizerShowGrid", out var visualizerShowGridStr);
            parent.WidgetManager.LoadSetting(this, "visualizerShowAxis", out var visualizerShowAxisStr);
            parent.WidgetManager.LoadSetting(this, "visualizerDensity", out var visualizerDensityStr);
            parent.WidgetManager.LoadSetting(this, "visualizerBgColor", out var visualizerBgColorStr);
            parent.WidgetManager.LoadSetting(this, "visualizerBarColor", out var visualizerBarColorStr);
            parent.WidgetManager.LoadSetting(this, "followWindowsDefault", out var followWindowsDefaultStr);

            // Actually parse and set settings
            try
            {
                _globalTheme = parent.WidgetManager.GlobalWidgetTheme;

                if (!string.IsNullOrEmpty(useGlobalThemeStr)) bool.TryParse(useGlobalThemeStr, out UseGlobalTheme);
                else UseGlobalTheme = parent.WidgetManager.PreferGlobalTheme;
                
                if (!string.IsNullOrEmpty(visualizerGraphTypeStr)) Enum.TryParse(visualizerGraphTypeStr, out VisualizerGraphType);
                else VisualizerGraphType = GraphType.BarGraph;
                
                if (!string.IsNullOrEmpty(visualizerShowGridStr)) bool.TryParse(visualizerShowGridStr, out VisualizerShowGrid);
                else VisualizerShowGrid = false;
                
                if (!string.IsNullOrEmpty(visualizerShowAxisStr)) bool.TryParse(visualizerShowAxisStr, out VisualizerShowAxis);
                else VisualizerShowAxis = false;
                
                if (!string.IsNullOrEmpty(visualizerDensityStr))
                {
                    int.TryParse(visualizerDensityStr, out int visualizerDensityTmp);
                    VisualizerDensity = Math.Min(300, Math.Max(visualizerDensityTmp, 4));
                }
                else VisualizerDensity = 50;
                
                if (!string.IsNullOrEmpty(visualizerBgColorStr)) UserVisualizerBgColor = ColorTranslator.FromHtml(visualizerBgColorStr);
                else UserVisualizerBgColor = System.Drawing.Color.FromArgb(0, 32, 63);
                
                if (!string.IsNullOrEmpty(visualizerBarColorStr)) UserVisualizerBarColor = ColorTranslator.FromHtml(visualizerBarColorStr);
                else UserVisualizerBarColor = System.Drawing.Color.FromArgb(173, 239, 209);

                if (!string.IsNullOrEmpty(followWindowsDefaultStr)) bool.TryParse(followWindowsDefaultStr, out FollowWindowsDefaultDevice);
                else FollowWindowsDefaultDevice = true; // Enable by default for better user experience

                UpdateSettings();
                SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading settings");
            }
        }

        private string FindDefaultDevice()
        {
            Logger.Debug("Looking for default device");
            
            if (AudioDeviceSource.Devices.Count == 0)
            {
                Logger.Debug("No devices found in source");
                return string.Empty;
            }

            // First check if we should follow Windows default device
            if (FollowWindowsDefaultDevice)
            {
                string defaultId = AudioDeviceSource.ActivePlaybackDevice;
                if (!string.IsNullOrEmpty(defaultId))
                {
                    Logger.Debug($"Using Windows default device: {defaultId}");
                    return defaultId;
                }
            }

            // Next, try the preferred device if set
            if (!string.IsNullOrEmpty(PreferredDeviceID))
            {
                AudioDeviceInfo preferredDevice = AudioDeviceSource.Devices.FirstOrDefault(d => d.ID == PreferredDeviceID);
                if (preferredDevice != null)
                {
                    Logger.Debug($"Found preferred device: {preferredDevice.DisplayName}, {{{preferredDevice.ID}}}");
                    return PreferredDeviceID;
                }
            }

            // Finally, fall back to any default or first available device
            string deviceId = AudioDeviceSource.DefaultDevice ?? AudioDeviceSource.Devices[0]?.ID ?? string.Empty;

            if (string.IsNullOrEmpty(deviceId))
            {
                Logger.Debug("No default device found");
                return string.Empty;
            }
            else
            {
                Logger.Debug($"Found default device: {deviceId}");
                return deviceId;
            }
        }

        public bool HandleInputDeviceChange(string newId)
        {
            lock (_deviceLock)
            {
                // Null check
                if (string.IsNullOrEmpty(newId))
                {
                    Logger.Debug("HandleInputDeviceChange: Invalid device ID!");
                    return false;
                }

                // Dispose any previous handlers and analyzers
                CleanupAudioComponents();

                try
                {
                    // Create new handler and analyzer
                    Logger.Debug($"HandleInputDeviceChange: Creating new handler for device {newId}...");
                    _audioDeviceHandler = AudioDeviceSource.CreateHandler(newId);
                    _audioDataAnalyzer = new AudioDataAnalyzer(_audioDeviceHandler);

                    // Hook into analyzer
                    Logger.Debug("HandleInputDeviceChange: Hooking into analyzer...");
                    _audioDataAnalyzer.Update += Update;

                    // Initialize data storage
                    InitDataStorage(_audioDataAnalyzer, _audioDeviceHandler);

                    // Start handler
                    Logger.Debug("HandleInputDeviceChange: Starting handler...");
                    _audioDeviceHandler.Start();

                    // Update device tracking
                    _activeDeviceID = newId;
                    SelectedDeviceID = newId;
                    
                    // Update device name in UI if possible
                    string deviceName = AudioDeviceSource.GetDeviceNameById(newId);
                    if (!string.IsNullOrEmpty(deviceName))
                    {
                        Logger.Debug($"Connected to device: {deviceName}");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "An error occurred while trying to hook into the audio device handler.");
                    return false;
                }
            }
        }

        private void CleanupAudioComponents()
        {
            // Dispose any previous handlers
            Logger.Debug("CleanupAudioComponents: Disposing previous handler...");
            if (_audioDeviceHandler != null)
            {
                _audioDeviceHandler.Dispose();
                _audioDeviceHandler = null;
            }

            // Dispose any previous analyzers
            if (_audioDataAnalyzer != null)
            {
                Logger.Debug("CleanupAudioComponents: Disposing previous analyzer...");
                _audioDataAnalyzer.Update -= Update;
                _audioDataAnalyzer = null;
            }
        }

        private void InitDataStorage(AudioDataAnalyzer analyzer, AudioDeviceHandler handler)
        {
            Logger.Debug("InitDataStorage: Initializing data storage...");
            lock (_frequencyDataLock)
            {
                _frequencyDataSeries = new Dictionary<int, double>(analyzer.FftDataPoints);
                _frequencyDataSeries.Append<int, double>(analyzer.FftIndicesList, analyzer.DbValuesList);
            }
        }

        // For event hook
        private void Update(object sender, EventArgs e)
        {
            Update((AudioDataAnalyzer)sender);
        }

        private void Update(AudioDataAnalyzer analyzer)
        {
            lock (_frequencyDataLock)
            {
                _frequencyDataSeries.Clear();
                _frequencyDataSeries.Append<int, double>(analyzer.FftIndicesList, analyzer.DbValuesList);
            }
        }

        /// <summary>
        /// Clears widget
        /// </summary>
        public void ClearWidget(string status = "")
        {
            if (_bitmapCurrent == null || _bitmapLock == null)
                return;
                
            bool lockAcquired = false;
            try
            {
                lockAcquired = _bitmapLock.WaitOne(mutex_timeout);
                if (!lockAcquired)
                    return;
                    
                try
                {
                    using (Graphics g = Graphics.FromImage(_bitmapCurrent))
                    {
                        g.Clear(_visualizerBgColor);

                        if (!string.IsNullOrEmpty(status))
                        {
                            RectangleF layoutRectangle =
                                new RectangleF(0, 0, WidgetSize.ToSize().Width, WidgetSize.ToSize().Height);
                            using (var brush = new SolidBrush(_visualizerBarColor))
                            {
                                g.DrawString(status, new Font("Lucida Console", 16, System.Drawing.FontStyle.Bold), brush, layoutRectangle);
                            }
                        }
                    }

                    UpdateWidget();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error clearing widget");
                }
            }
            catch (ObjectDisposedException)
            {
                // Mutex was already disposed, ignore
                Logger.Debug("Mutex already disposed in ClearWidget");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error waiting for mutex in ClearWidget");
            }
            finally
            {
                if (lockAcquired && _bitmapLock != null)
                {
                    try
                    {
                        _bitmapLock.ReleaseMutex();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Mutex was disposed after we acquired it
                        Logger.Debug("Mutex was disposed after acquisition in ClearWidget");
                    }
                    catch (ApplicationException)
                    {
                        // Not owning the mutex - this shouldn't happen but handle it anyway
                        Logger.Debug("ApplicationException releasing mutex in ClearWidget");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error releasing mutex in ClearWidget");
                    }
                }
            }
        }

        /// <summary>
        /// Main drawing method
        /// </summary>
        public void DrawWidget()
        {
            // Don't draw if no data or if mutex is null
            if (_bitmapCurrent == null || _bitmapLock == null)
            {
                return;
            }

            if (_noDevices)
            {
                ClearWidget("No supported devices!");
                return;
            }

            if (string.IsNullOrEmpty(_activeDeviceID))
            {
                ClearWidget("No device selected!");
                return;
            }

            Dictionary<int, double> frequencyDataCopy;
            lock (_frequencyDataLock)
            {
                if (_frequencyDataSeries.Count <= 0)
                {
                    ClearWidget();
                    return;
                }

                // Create a copy of the frequency data to avoid holding the lock during drawing
                frequencyDataCopy = new Dictionary<int, double>(_frequencyDataSeries);
            }

            // Check drawing conditions and frequency data values
            bool lockAcquired = false;
            try
            {
                lockAcquired = _bitmapLock.WaitOne(mutex_timeout);
                if (!lockAcquired)
                    return;
                    
                try
                {
                    using (Graphics g = Graphics.FromImage(_bitmapCurrent))
                    {
                        // Clear board to draw visualizer
                        g.Clear(_visualizerBgColor);
                    }

                    // Clean up Infinity/NaN values
                    foreach (var key in frequencyDataCopy.Keys.ToList())
                    {
                        if (double.IsInfinity(frequencyDataCopy[key]) || double.IsNaN(frequencyDataCopy[key]))
                        {
                            frequencyDataCopy[key] = 0;
                        }
                    }

                    // Draw graph in log10 scale between 20Hz and 25kHz. Y axis is from -200 to 0.
                    var plt = new Plot(WidgetSize.ToSize().Width, WidgetSize.ToSize().Height);

                    var xAxisData = Tools.Log10(frequencyDataCopy.Keys.Select(i => (double)i).ToArray());
                    var yAxisData = frequencyDataCopy.Values.Select(d => d + 200 > 190 ? 0 : d + 200).ToArray();

                    var plotData = GetInterpolatedData(xAxisData, yAxisData, VisualizerDensity);

                    switch (VisualizerGraphType)
                    {
                        default:
                        case GraphType.BarGraph:
                            var bars = plt.AddBar(plotData.Item2, _visualizerBarColor);
                            bars.BorderLineWidth = 0;
                            bars.BarWidth = 1.05;
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
                    }
                    else
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
                    
                    // Flush
                    UpdateWidget();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error in DrawWidget");
                }
            }
            catch (ObjectDisposedException)
            {
                // Mutex was already disposed, ignore
                Logger.Debug("Mutex already disposed in DrawWidget");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error waiting for mutex in DrawWidget");
            }
            finally
            {
                // Release bitmap lock
                if (lockAcquired && _bitmapLock != null)
                {
                    try 
                    {
                        _bitmapLock.ReleaseMutex();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Mutex was already disposed, ignore
                        Logger.Debug("Mutex already disposed in DrawWidget");
                    }
                    catch (ApplicationException)
                    {
                        // Not owning the mutex - this can happen if we never acquired it
                        Logger.Debug("ApplicationException releasing mutex in DrawWidget");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error releasing mutex in DrawWidget");
                    }
                }
            }
        }

        private (double[], double[]) GetInterpolatedData(double[] xData, double[] yData, int numPoints)
        {
            if (xData == null || yData == null || xData.Length == 0 || yData.Length == 0 || numPoints <= 0)
            {
                return (new double[0], new double[0]);
            }

            try
            {
                // Transform x-axis data to logarithmic scale
                var logData = new double[xData.Length];
                for (int i = 0; i < xData.Length; i++)
                {
                    logData[i] = Math.Log(Math.Max(xData[i], 0.01), 10);
                    // add check to replace negative or zero data with small positive value
                    if (logData[i] <= 0)
                    {
                        logData[i] = 0.01 * (i + 1);
                    }
                }

                // Perform linear spline interpolation
                var interpolator = LinearSpline.InterpolateSorted(logData, yData);
                var xInterp = new double[numPoints];
                var yInterp = new double[numPoints];
                
                if (logData.Length > 1)
                {
                    var step = (logData.Last() - logData.First()) / (numPoints - 1);

                    for (int i = 0; i < numPoints; i++)
                    {
                        var x = logData.First() + (i * step);
                        xInterp[i] = Math.Pow(10, x);
                        yInterp[i] = interpolator.Interpolate(x);

                        // add check to replace NaN data with zero
                        if (double.IsNaN(yInterp[i]) || double.IsInfinity(yInterp[i]))
                        {
                            yInterp[i] = 0;
                        }
                    }
                }

                return (xInterp, yInterp);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in GetInterpolatedData");
                return (new double[0], new double[0]);
            }
        }

        /// <summary>
        /// Flushes the buffer bitmap to widget
        /// </summary>
        private void UpdateWidget()
        {
            if (_bitmapCurrent == null)
            {
                return;
            }

            try
            {
                WidgetUpdatedEventArgs e = new WidgetUpdatedEventArgs
                {
                    WidgetBitmap = _bitmapCurrent,
                    WaitMax = 1000
                };

                WidgetUpdated?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in UpdateWidget");
            }
        }

        /// <summary>
        /// Widget functionality methods
        /// </summary>
        public void RequestUpdate()
        {
            if (_bitmapLock == null)
                return;
                
            bool lockAcquired = false;
            try
            {
                lockAcquired = _bitmapLock.WaitOne(mutex_timeout);
                if (lockAcquired)
                {
                    UpdateWidget();
                }
            }
            catch (ObjectDisposedException)
            {
                // Mutex was already disposed, ignore
                Logger.Debug("Mutex already disposed in RequestUpdate");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error waiting for mutex in RequestUpdate");
            }
            finally
            {
                if (lockAcquired && _bitmapLock != null)
                {
                    try
                    {
                        _bitmapLock.ReleaseMutex();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Mutex was disposed after we acquired it
                        Logger.Debug("Mutex was disposed after acquisition in RequestUpdate");
                    }
                    catch (ApplicationException)
                    {
                        // Not owning the mutex - this shouldn't happen but handle it anyway
                        Logger.Debug("ApplicationException releasing mutex in RequestUpdate");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error releasing mutex in RequestUpdate");
                    }
                }
            }
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
            Init();
            _pauseDrawing = false;
        }

        public void Dispose()
        {
            bool releaseMutex = false;
            Mutex localBitmapLock = null;
            
            try
            {
                // First stop background activity
                _runTask = false;
                _pauseDrawing = true;
                
                // Make a local copy of the mutex reference before we try to acquire it
                // This avoids race conditions where the mutex gets disposed during the operation
                localBitmapLock = Interlocked.CompareExchange(ref _bitmapLock, null, _bitmapLock);
                
                if (localBitmapLock != null)
                {
                    try
                    {
                        // Try to acquire the mutex with a shorter timeout
                        releaseMutex = localBitmapLock.WaitOne(500);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Mutex was already disposed, ignore and continue
                        Logger.Debug("Mutex already disposed during disposal");
                        releaseMutex = false;
                    }
                    catch (AbandonedMutexException)
                    {
                        // Abandoned mutex - we now own it, so we should release it
                        Logger.Debug("Abandoned mutex encountered during disposal");
                        releaseMutex = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error waiting for mutex in Dispose");
                        releaseMutex = false;
                    }
                }

                // Unhook event handlers
                if (parent?.WidgetManager != null)
                {
                    parent.WidgetManager.GlobalThemeUpdated -= UpdateSettings;
                }
                
                if (AudioDeviceSource != null)
                {
                    AudioDeviceSource.DefaultDeviceChanged -= AudioDeviceSource_DefaultDeviceChanged;
                    AudioDeviceSource.DevicesChanged -= AudioDeviceSource_DevicesChanged;
                    AudioDeviceSource.DeviceFormatChanged -= AudioDeviceSource_FormatChanged;
                }

                CleanupAudioComponents();
                
                // Dispose AudioDeviceSource
                if (AudioDeviceSource != null)
                {
                    AudioDeviceSource.Dispose();
                    AudioDeviceSource = null;
                }

                // Dispose bitmap
                if (_bitmapCurrent != null)
                {
                    _bitmapCurrent.Dispose();
                    _bitmapCurrent = null;
                }

                // Dispose font
                if (_visualizerFont != null)
                {
                    _visualizerFont.Dispose();
                    _visualizerFont = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in Dispose");
            }
            finally
            {
                // Release the mutex if we successfully acquired it
                if (releaseMutex && localBitmapLock != null)
                {
                    try
                    {
                        localBitmapLock.ReleaseMutex();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Mutex was disposed after we acquired it
                        Logger.Debug("Mutex was disposed after acquisition in Dispose");
                    }
                    catch (ApplicationException)
                    {
                        // Not owning the mutex - this shouldn't happen but handle it anyway
                        Logger.Debug("ApplicationException releasing mutex in Dispose");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error releasing mutex in Dispose");
                    }
                }
                
                // Always dispose the mutex at the end
                try
                {
                    if (localBitmapLock != null)
                    {
                        localBitmapLock.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing mutex in Dispose");
                }
                
                // Set the field to null to indicate it's been disposed
                _bitmapLock = null;
            }
        }

        public UserControl GetSettingsControl() => new SettingsControl(this);
    }
}
