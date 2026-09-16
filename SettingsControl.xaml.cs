using NLog;
using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AudioVisualizerWidget
{
    /// <summary>
    /// Interaction logic for SettingsControl.xaml
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly WidgetInstance _parent;
        private string deviceId = string.Empty;
        private string deviceName = string.Empty;
        private bool _isInitialized = false;

        public SettingsControl(WidgetInstance parent)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            this._parent = parent;
            
            try
            {
                InitializeComponent();
                Loaded += OnLoad;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing SettingsControl");
            }
        }

        private void OnLoad(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prevent re-initialization if already loaded
                if (_isInitialized)
                    return;

                // Setup graph type selection
                InitializeGraphTypeSelector();

                // Setup device selection
                InitializeDeviceSelector();

                // Set other controls
                InitializeOtherControls();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in OnLoad");
            }
        }

        private void InitializeGraphTypeSelector()
        {
            if (graphSelect == null)
                return;

            try
            {
                graphSelect.ItemsSource = Enum.GetValues(typeof(GraphType));
                graphSelect.SelectedValue = _parent.VisualizerGraphType;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing graph type selector");
            }
        }

        private void InitializeDeviceSelector()
        {
            if (deviceSelect == null || _parent.AudioDeviceSource == null)
                return;

            try
            {
                deviceSelect.ItemsSource = _parent.AudioDeviceSource.Devices;
                deviceSelect.DisplayMemberPath = @"DisplayName";
                deviceSelect.SelectionChanged += DeviceSelect_SelectionChanged;
                deviceSelect.Text = _parent.PreferredDeviceName;

                // Set initial device ID and name
                deviceId = _parent.PreferredDeviceID;
                deviceName = _parent.PreferredDeviceName;
                
                // Update enabled state based on follow default setting
                UpdateDeviceSelectorState();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing device selector");
            }
        }

        private void UpdateDeviceSelectorState()
        {
            // Disable the device selector if we're following the Windows default
            if (deviceSelect != null && followDefaultCheck != null)
            {
                deviceSelect.IsEnabled = !followDefaultCheck.IsChecked.GetValueOrDefault();
            }
        }

        private void InitializeOtherControls()
        {
            try
            {
                if (globalThemeCheck != null)
                    globalThemeCheck.IsChecked = _parent.UseGlobalTheme;

                if (bgColorSelect != null)
                    bgColorSelect.Content = ColorTranslator.ToHtml(_parent.UserVisualizerBgColor);

                if (fgColorSelect != null)
                    fgColorSelect.Content = ColorTranslator.ToHtml(_parent.UserVisualizerBarColor);

                if (vdSlider != null)
                    vdSlider.Value = _parent.VisualizerDensity;

                if (normalizeCheck != null)
                    normalizeCheck.IsChecked = _parent.VisualizerNormalize;

                if (gridCheck != null)
                    gridCheck.IsChecked = _parent.VisualizerShowGrid;

                if (axisCheck != null)
                    axisCheck.IsChecked = _parent.VisualizerShowAxis;
                
                if (followDefaultCheck != null)
                    followDefaultCheck.IsChecked = _parent.FollowWindowsDefaultDevice;

                UpdateColorButtonsState();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing other controls");
            }
        }

        private void UpdateColorButtonsState()
        {
            if (bgColorSelect != null && fgColorSelect != null)
            {
                bool enabled = !_parent.UseGlobalTheme;
                bgColorSelect.IsEnabled = enabled;
                fgColorSelect.IsEnabled = enabled;
            }
        }

        private void fgColorSelect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button caller && _parent?.WidgetObject?.WidgetManager != null)
                {
                    var contentStr = caller.Content?.ToString();
                    if (!string.IsNullOrEmpty(contentStr))
                    {
                        Color defaultColor = ColorTranslator.FromHtml(contentStr);
                        Color selectedColor = _parent.WidgetObject.WidgetManager.RequestColorSelection(defaultColor);
                        caller.Content = ColorTranslator.ToHtml(selectedColor);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in foreground color selection");
            }
        }

        private void bgColorSelect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button caller && _parent?.WidgetObject?.WidgetManager != null)
                {
                    var contentStr = caller.Content?.ToString();
                    if (!string.IsNullOrEmpty(contentStr))
                    {
                        Color defaultColor = ColorTranslator.FromHtml(contentStr);
                        Color selectedColor = _parent.WidgetObject.WidgetManager.RequestColorSelection(defaultColor);
                        caller.Content = ColorTranslator.ToHtml(selectedColor);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in background color selection");
            }
        }

        private void DeviceSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (deviceSelect.SelectedValue is AudioDeviceInfo deviceInfo)
                {
                    deviceId = deviceInfo.ID;
                    deviceName = deviceInfo.DisplayName;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in device selection changed");
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Update follow Windows default setting
                _parent.FollowWindowsDefaultDevice = followDefaultCheck?.IsChecked == true;
                
                // Update device info if not following Windows default
                if (!_parent.FollowWindowsDefaultDevice && !string.IsNullOrEmpty(deviceId))
                {
                    _parent.PreferredDeviceID = deviceId;
                    _parent.PreferredDeviceName = deviceName;
                    _parent.HandleInputDeviceChange(deviceId);
                }
                else if (_parent.FollowWindowsDefaultDevice)
                {
                    // If following Windows default, use the current default device
                    string defaultDeviceId = _parent.AudioDeviceSource.ActivePlaybackDevice;
                    if (!string.IsNullOrEmpty(defaultDeviceId))
                    {
                        _parent.HandleInputDeviceChange(defaultDeviceId);
                    }
                }

                // Update visualizer settings
                if (graphSelect?.SelectedValue != null)
                    _parent.VisualizerGraphType = (GraphType)graphSelect.SelectedValue;

                _parent.UseGlobalTheme = globalThemeCheck?.IsChecked == true;
                
                if (vdSlider != null)
                    _parent.VisualizerDensity = (int)vdSlider.Value;
                
                _parent.VisualizerNormalize = normalizeCheck?.IsChecked == true;
                _parent.VisualizerShowGrid = gridCheck?.IsChecked == true;
                _parent.VisualizerShowAxis = axisCheck?.IsChecked == true;

                // Update colors
                if (fgColorSelect?.Content is string fgColor)
                    _parent.UserVisualizerBarColor = ColorTranslator.FromHtml(fgColor);
                
                if (bgColorSelect?.Content is string bgColor)
                    _parent.UserVisualizerBgColor = ColorTranslator.FromHtml(bgColor);

                // Apply and save settings
                _parent.UpdateSettings();
                _parent.SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save settings from settings control");
                MessageBox.Show("Failed to save settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void globalThemeCheck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _parent.UseGlobalTheme = globalThemeCheck?.IsChecked ?? false;
                UpdateColorButtonsState();

                _parent.UpdateSettings();
                _parent.SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating global theme setting");
            }
        }
        
        private void followDefaultCheck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool followDefault = followDefaultCheck?.IsChecked ?? false;
                _parent.FollowWindowsDefaultDevice = followDefault;
                
                // Update the device selector state
                UpdateDeviceSelectorState();
                
                // If we're now following the default, switch to the default device immediately
                if (followDefault)
                {
                    string defaultDeviceId = _parent.AudioDeviceSource.ActivePlaybackDevice;
                    if (!string.IsNullOrEmpty(defaultDeviceId))
                    {
                        _parent.HandleInputDeviceChange(defaultDeviceId);
                    }
                }
                
                _parent.SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating follow default setting");
            }
        }
    }
}
