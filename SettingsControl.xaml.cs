using NLog;
using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AudioVisualizerWidget
{
    /// <summary>
    /// Interaction logic for SettingsControl.xaml
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
        private WidgetInstance _parent;
        public SettingsControl(WidgetInstance parent)
        {
            this._parent = parent;
            Loaded += OnLoad;
            InitializeComponent();
        }

        private void OnLoad(object sender, RoutedEventArgs e)
        {
            graphSelect.ItemsSource = Enum.GetValues(typeof(GraphType));
            graphSelect.SelectedValue = _parent.VisualizerGraphType;

            deviceSelect.ItemsSource = _parent.AudioDeviceSource.Devices;
            deviceSelect.DisplayMemberPath = @"DisplayName";
            deviceSelect.SelectionChanged += DeviceSelect_SelectionChanged;
            deviceSelect.Text = _parent.PreferredDeviceName;

            globalThemeCheck.IsChecked = _parent.UseGlobalTheme;
            bgColorSelect.Content = ColorTranslator.ToHtml(_parent.UserVisualizerBgColor);
            fgColorSelect.Content = ColorTranslator.ToHtml(_parent.UserVisualizerBarColor);
            vdSlider.Value = _parent.VisualizerDensity;
            normalizeCheck.IsChecked = _parent.VisualizerNormalize;
            gridCheck.IsChecked = _parent.VisualizerShowGrid;
            axisCheck.IsChecked = _parent.VisualizerShowAxis;

            bgColorSelect.IsEnabled = !_parent.UseGlobalTheme;
            fgColorSelect.IsEnabled = !_parent.UseGlobalTheme;
        }

        private void fgColorSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button caller)
            {
                Color defaultColor = ColorTranslator.FromHtml(caller.Content.ToString());
                Color selectedColor = _parent.WidgetObject.WidgetManager.RequestColorSelection(defaultColor);
                caller.Content = ColorTranslator.ToHtml(selectedColor);
            }
        }

        private void bgColorSelect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button caller)
            {
                Color defaultColor = ColorTranslator.FromHtml(caller.Content.ToString());
                Color selectedColor = _parent.WidgetObject.WidgetManager.RequestColorSelection(defaultColor);
                caller.Content = ColorTranslator.ToHtml(selectedColor);
            }
        }

        private string deviceId = string.Empty;
        private string deviceName = string.Empty;

        private void DeviceSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (deviceSelect.SelectedValue is AudioDeviceInfo deviceInfo)
            {
                deviceId = deviceInfo.ID;
                deviceName = deviceInfo.DisplayName;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _parent.PreferredDeviceID = deviceId;
                _parent.PreferredDeviceName = deviceName;
                _parent.HandleInputDeviceChange(deviceId);

                _parent.VisualizerGraphType = (GraphType)graphSelect.SelectedValue;
                _parent.UseGlobalTheme = globalThemeCheck.IsChecked == true;
                _parent.VisualizerDensity = (int)vdSlider.Value;
                _parent.VisualizerNormalize = normalizeCheck.IsChecked == true;
                _parent.VisualizerShowGrid = gridCheck.IsChecked == true;
                _parent.VisualizerShowAxis = axisCheck.IsChecked == true;

                _parent.UserVisualizerBarColor = ColorTranslator.FromHtml(fgColorSelect.Content as string);
                _parent.UserVisualizerBgColor = ColorTranslator.FromHtml(bgColorSelect.Content as string);

                _parent.UpdateSettings();
                _parent.SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to run save settings from settings control");
            }
        }

        private void globalThemeCheck_Click(object sender, RoutedEventArgs e)
        {
            _parent.UseGlobalTheme = globalThemeCheck.IsChecked ?? false;
            bgColorSelect.IsEnabled = !_parent.UseGlobalTheme;
            fgColorSelect.IsEnabled = !_parent.UseGlobalTheme;

            _parent.UpdateSettings();
            _parent.SaveSettings();
        }
    }
}
