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
            Loaded += OnLoad;
            InitializeComponent();
        }

        private void OnLoad(object sender, RoutedEventArgs e)
        {
            bgColor.Text = ColorTranslator.ToHtml(Parent.visualizerBgColor);
            fgColor.Text = ColorTranslator.ToHtml(Parent.visualizerBarColor);
            vdSlider.Value = Parent.visualizerDensity;
            vmSlider.Value = Parent.visualizerMultiplier;
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
