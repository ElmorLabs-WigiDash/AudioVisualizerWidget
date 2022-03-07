using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;

namespace AudioVisualizerWidget
{
    /// <summary>
    /// Interaction logic for SettingsControl.xaml
    /// </summary>
    public partial class SettingsUserControl : UserControl
    {
        private WidgetInstance Parent;
        public SettingsUserControl(WidgetInstance parent)
        {
            Parent = parent;

            InitializeComponent();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Parent.visualizerBgColor = ColorTranslator.FromHtml(bgColor.Text);
                Parent.visualizerBarColor = ColorTranslator.FromHtml(fgColor.Text);
                Parent.visualizerDensity = (int)vdSlider.Value;
                Parent.visualizerMultiplier = (int)vmSlider.Value;
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
