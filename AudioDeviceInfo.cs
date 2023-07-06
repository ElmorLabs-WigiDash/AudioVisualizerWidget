using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioVisualizerWidget
{
    public class AudioDeviceInfo : INotifyPropertyChanged
    {
        private string _displayName;
        private string _id;

        public AudioDeviceInfo(MMDevice device)
        {
            _displayName = device.DeviceFriendlyName;
            _id = device.ID;
        }

        public string DisplayName
        {
            get { return _displayName; }
            set
            {
                _displayName = value;
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string ID
        {
            get { return _id; }
            set
            {
                _id = value;
                OnPropertyChanged(nameof(ID));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        internal void Update(MMDevice device)
        {
            _displayName = device.DeviceFriendlyName;
        }
    }
}
