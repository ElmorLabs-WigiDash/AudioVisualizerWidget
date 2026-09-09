using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NLog;

namespace AudioVisualizerWidget
{
    public class AudioDeviceInfo : INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private string _displayName;
        private string _id;
        private readonly object _updateLock = new object();
        private Dispatcher _dispatcher;
        private int _isUpdating = 0;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private const int MinUpdateIntervalMs = 250; // Minimum time between updates to prevent flooding

        public AudioDeviceInfo(MMDevice device)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            try
            {
                _displayName = device.DeviceFriendlyName;
                _id = device.ID;
                _dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing AudioDeviceInfo");
                _displayName = "Unknown Device";
                _id = Guid.NewGuid().ToString();
            }
        }

        public string DisplayName
        {
            get { return _displayName; }
            private set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    NotifyPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string ID
        {
            get { return _id; }
            private set
            {
                if (_id != value)
                {
                    _id = value;
                    NotifyPropertyChanged(nameof(ID));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void NotifyPropertyChanged(string propertyName)
        {
            if (_dispatcher != null && !_dispatcher.CheckAccess())
            {
                try
                {
                    _dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(propertyName)));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error dispatching property change notification");
                }
            }
            else
            {
                OnPropertyChanged(propertyName);
            }
        }

        internal void Update(MMDevice device)
        {
            if (device == null)
                return;

            // Don't allow updates too frequently to prevent UI freezing
            var now = DateTime.Now;
            if ((now - _lastUpdateTime).TotalMilliseconds < MinUpdateIntervalMs)
                return;

            // Use a non-blocking check to see if we're already updating
            if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) == 1)
                return;

            try
            {
                _lastUpdateTime = now;
                
                // Copy values outside the lock to minimize lock time
                string newName;
                
                try
                {
                    newName = device.DeviceFriendlyName;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error getting device friendly name");
                    newName = "Error: " + device.ID;
                }

                // Use a timeout to prevent deadlocks
                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(_updateLock, 50, ref lockTaken);
                    
                    if (lockTaken)
                    {
                        if (_displayName != newName)
                        {
                            DisplayName = newName;
                        }
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        Monitor.Exit(_updateLock);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating audio device info");
            }
            finally
            {
                Interlocked.Exchange(ref _isUpdating, 0);
            }
        }
    }
}
