using NAudio.CoreAudioApi.Interfaces;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using WigiDashWidgetFramework;

namespace AudioVisualizerWidget
{
    public class AudioDeviceSource : IDisposable, IMMNotificationClient
    {
        private MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private Dispatcher _dispatcher;

        public ObservableCollection<AudioDeviceInfo> Devices { get; } = new ObservableCollection<AudioDeviceInfo>();
        public event EventHandler DevicesChanged;

        public string DefaultDevice { get; private set; }

        public AudioDeviceSource()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _enumerator.RegisterEndpointNotificationCallback(this);
            RefreshDevices();
        }

        public AudioDeviceHandler CreateHandler(string id)
        {
            var device = _enumerator.GetDevice(id);
            return new AudioDeviceHandler(device);
        }

        private void RefreshDevices()
        {
            Logger.Info("Refreshing Audio Devices");
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke((Action)RefreshDevices);
                return;
            }

            DefaultDevice = GetDefaultDevice();

            var deviceMap = Devices.ToDictionary(d => d.ID, d => d);
            var presentDevices = new HashSet<string>();

            Logger.Debug("Enumerating Active Audio Devices");
            foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                presentDevices.Add(d.ID);
                if (deviceMap.TryGetValue(d.ID, out var device))
                {
                    device.Update(d);
                }
                else
                {
                    Devices.Add(new AudioDeviceInfo(d));
                }
                d.Dispose();
            }

            if (presentDevices.Count == 0)
            {
                Logger.Warn("No Active Audio Devices Found");
            }

            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (!presentDevices.Contains(Devices[i].ID))
                {
                    Devices.RemoveAt(i);
                }
            }

            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        private string GetDefaultDevice()
        {
            Logger.Info("Getting Default Audio Device");
            if (!_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                Logger.Warn("No Default Audio Render Endpoint");
                return null;
            }
            using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                Logger.Info($"Default Audio Device: {device.ID}");
                return device.ID;
            }
        }

        public void Dispose()
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _enumerator.Dispose();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            RefreshDevices();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            RefreshDevices();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            RefreshDevices();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            RefreshDevices();
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            RefreshDevices();
        }
    }
}
