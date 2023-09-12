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
        private WidgetInstance Parent;
        public SettingsControl(WidgetInstance parent)
        {
            Parent = parent;
            Loaded += OnLoad;
            InitializeComponent();
        }

        private void OnLoad(object sender, RoutedEventArgs e)
        {
            graphSelect.ItemsSource = Enum.GetValues(typeof(GraphType));
            graphSelect.SelectedValue = Parent.VisualizerGraphType;

            deviceSelect.ItemsSource = Parent.AudioDeviceSource.Devices;
            deviceSelect.DisplayMemberPath = "DisplayName";
            deviceSelect.SelectionChanged += DeviceSelect_SelectionChanged;
            deviceSelect.Text = Parent.PreferredDeviceName;

            globalThemeCheck.IsChecked = Parent.UseGlobalTheme;
            bgColor.Text = ColorTranslator.ToHtml(Parent.UserVisualizerBgColor);
            fgColor.Text = ColorTranslator.ToHtml(Parent.UserVisualizerBarColor);
            vdSlider.Value = Parent.VisualizerDensity;
            normalizeCheck.IsChecked = Parent.VisualizerNormalize;
            gridCheck.IsChecked = Parent.VisualizerShowGrid;
            axisCheck.IsChecked = Parent.VisualizerShowAxis;
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
                Parent.PreferredDeviceID = deviceId;
                Parent.PreferredDeviceName = deviceName;
                Parent.HandleInputDeviceChange(deviceId);

                Parent.VisualizerGraphType = (GraphType)graphSelect.SelectedValue;
                Parent.UseGlobalTheme = globalThemeCheck.IsChecked == true;
                Parent.UserVisualizerBgColor = ColorTranslator.FromHtml(bgColor.Text);
                Parent.UserVisualizerBarColor = ColorTranslator.FromHtml(fgColor.Text);
                Parent.VisualizerDensity = (int)vdSlider.Value;
                Parent.VisualizerNormalize = normalizeCheck.IsChecked == true;
                Parent.VisualizerShowGrid = gridCheck.IsChecked == true;
                Parent.VisualizerShowAxis = axisCheck.IsChecked == true;
                Parent.UpdateSettings();
                Parent.SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
