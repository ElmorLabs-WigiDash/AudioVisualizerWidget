using System;
using System.Drawing;
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
            deviceSelect.SelectedValuePath = "ID";
            deviceSelect.DisplayMemberPath = "DisplayName";
            deviceSelect.SelectedValue = Parent.SelectedDeviceID;

            globalThemeCheck.IsChecked = Parent.UseGlobalTheme;
            bgColor.Text = ColorTranslator.ToHtml(Parent.UserVisualizerBgColor);
            fgColor.Text = ColorTranslator.ToHtml(Parent.UserVisualizerBarColor);
            vdSlider.Value = Parent.VisualizerDensity;
            normalizeCheck.IsChecked = Parent.VisualizerNormalize;
            gridCheck.IsChecked = Parent.VisualizerShowGrid;
            axisCheck.IsChecked = Parent.VisualizerShowAxis;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (deviceSelect.SelectedValue != null && string.IsNullOrEmpty((string)deviceSelect.SelectedValue)) Parent.HandleInputDeviceChange((string)deviceSelect.SelectedValue);
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
