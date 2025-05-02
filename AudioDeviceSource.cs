using NAudio.CoreAudioApi.Interfaces;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using NLog;
using System.Threading;

namespace AudioVisualizerWidget
{
    public class AudioDeviceSource : IDisposable, IMMNotificationClient
    {
        private MMDeviceEnumerator _enumerator;
        private Dispatcher _dispatcher;
        private readonly object _deviceLock = new object();
        private readonly object _deviceRefreshLock = new object();
        private bool _isDisposed = false;
        private Timer _deviceCheckTimer;
        private const int DeviceCheckIntervalMs = 5000; // Check every 5 seconds
        private DateTime _lastRefreshTime = DateTime.MinValue;
        private const int MinRefreshIntervalMs = 500; // Minimum time between refreshes
        private int _isRefreshing = 0;
        private int _consecutiveRefreshErrors = 0;
        private const int MaxConsecutiveErrors = 3;

        // Add dictionary to track device formats
        private Dictionary<string, string> _deviceFormats = new Dictionary<string, string>();

        public ObservableCollection<AudioDeviceInfo> Devices { get; } = new ObservableCollection<AudioDeviceInfo>();
        public event EventHandler DevicesChanged;
        public event EventHandler DefaultDeviceChanged;
        public event EventHandler<string> DeviceFormatChanged; // New event for format changes

        public string DefaultDevice { get; private set; }
        public string ActivePlaybackDevice => GetDefaultDevice(); // Always returns the current Windows default
        
        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        public AudioDeviceSource()
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _enumerator = new MMDeviceEnumerator();
                _enumerator.RegisterEndpointNotificationCallback(this);
                
                RefreshDevices();
                
                // Start a timer to periodically check for device changes
                // This provides a fallback for cases where Windows notifications fail
                _deviceCheckTimer = new Timer(CheckDevices, null, DeviceCheckIntervalMs, DeviceCheckIntervalMs);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing AudioDeviceSource");
            }
        }

        private void CheckDevices(object state)
        {
            try
            {
                if (_isDisposed)
                    return;

                // Only refresh if we're not already refreshing and enough time has passed
                if (_isRefreshing == 1 || (DateTime.Now - _lastRefreshTime).TotalMilliseconds < MinRefreshIntervalMs)
                    return;

                string currentDefault = GetDefaultDevice();
                if (currentDefault != DefaultDevice)
                {
                    // Force update if default device changed
                    Logger.Info($"Device check timer detected default device change: {DefaultDevice} -> {currentDefault}");
                    RefreshDevices();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in device check timer");
            }
        }

        public AudioDeviceHandler CreateHandler(string id)
        {
            if (_isDisposed)
            {
                Logger.Error("Attempted to create handler after AudioDeviceSource was disposed");
                throw new ObjectDisposedException(nameof(AudioDeviceSource));
            }

            // If no specific ID is requested, use the current Windows default device
            if (string.IsNullOrEmpty(id))
            {
                Logger.Info("No specific device ID provided, using current Windows default device");
                id = GetDefaultDevice();
                if (string.IsNullOrEmpty(id))
                {
                    Logger.Error("No default device available");
                    throw new InvalidOperationException("No audio device available");
                }
            }

            try
            {
                MMDevice device = null;
                
                // Use a timeout to avoid blocking indefinitely
                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(_deviceLock, 1000, ref lockTaken);
                    
                    if (!lockTaken)
                    {
                        Logger.Warn("Could not acquire device lock for CreateHandler - timeout exceeded");
                        throw new TimeoutException("Could not acquire device lock in a reasonable time");
                    }
                    
                    device = _enumerator.GetDevice(id);
                }
                finally
                {
                    if (lockTaken)
                    {
                        Monitor.Exit(_deviceLock);
                    }
                }
                
                return new AudioDeviceHandler(device);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to create handler for device ID: {id}");
                
                // If requested device fails, try default device as fallback
                if (id != GetDefaultDevice())
                {
                    string defaultId = GetDefaultDevice();
                    if (!string.IsNullOrEmpty(defaultId))
                    {
                        Logger.Info($"Attempting to use default device {defaultId} as fallback");
                        try
                        {
                            bool lockTaken = false;
                            try
                            {
                                Monitor.TryEnter(_deviceLock, 1000, ref lockTaken);
                                
                                if (!lockTaken)
                                {
                                    Logger.Warn("Could not acquire device lock for fallback CreateHandler - timeout exceeded");
                                    throw new TimeoutException("Could not acquire device lock in a reasonable time");
                                }
                                
                                var device = _enumerator.GetDevice(defaultId);
                                return new AudioDeviceHandler(device);
                            }
                            finally
                            {
                                if (lockTaken)
                                {
                                    Monitor.Exit(_deviceLock);
                                }
                            }
                        }
                        catch (Exception fallbackEx)
                        {
                            Logger.Error(fallbackEx, "Fallback to default device also failed");
                        }
                    }
                }
                
                throw;
            }
        }

        private void RefreshDevices()
        {
            if (_isDisposed)
                return;
                
            // Throttle refreshes to prevent UI lag
            var now = DateTime.Now;
            if ((now - _lastRefreshTime).TotalMilliseconds < MinRefreshIntervalMs)
                return;
                
            // Only allow one refresh at a time
            if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) == 1)
                return;

            try
            {
                _lastRefreshTime = now;
                
                if (!_dispatcher.CheckAccess())
                {
                    Logger.Debug("Dispatching RefreshDevices to UI thread");
                    _dispatcher.BeginInvoke(new Action(RefreshDevicesOnUiThread));
                    return;
                }
                
                RefreshDevicesOnUiThread();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error refreshing devices");
                _consecutiveRefreshErrors++;
                
                // If we're having consistent problems, increase the refresh interval temporarily
                if (_consecutiveRefreshErrors > MaxConsecutiveErrors)
                {
                    // Reset the timer with a longer interval
                    Logger.Warn($"Too many consecutive errors ({_consecutiveRefreshErrors}), increasing device check interval");
                    _deviceCheckTimer?.Change(DeviceCheckIntervalMs * 2, DeviceCheckIntervalMs * 2);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isRefreshing, 0);
            }
        }
        
        private void RefreshDevicesOnUiThread()
        {
            Logger.Info("Refreshing Audio Devices on UI thread");
            
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_deviceRefreshLock, 1000, ref lockTaken);
                
                if (!lockTaken)
                {
                    Logger.Warn("Could not acquire refresh lock - timeout exceeded");
                    return;
                }

                string oldDefault = DefaultDevice;
                DefaultDevice = GetDefaultDevice();
                bool defaultChanged = oldDefault != DefaultDevice;

                if (defaultChanged)
                {
                    Logger.Info($"Default device changed: {oldDefault} -> {DefaultDevice}");
                }

                var deviceMap = Devices.ToDictionary(d => d.ID, d => d);
                var presentDevices = new HashSet<string>();

                List<AudioDeviceInfo> devicesToAdd = new List<AudioDeviceInfo>();
                List<int> indexesToRemove = new List<int>();

                try
                {
                    Logger.Debug("Enumerating Active Audio Devices");
                    MMDeviceCollection devices = null;
                    
                    // Get device collection with timeout
                    bool deviceLockTaken = false;
                    try
                    {
                        Monitor.TryEnter(_deviceLock, 500, ref deviceLockTaken);
                        
                        if (!deviceLockTaken)
                        {
                            Logger.Warn("Could not acquire device lock for enumeration - timeout exceeded");
                            return;
                        }
                        
                        devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    }
                    finally
                    {
                        if (deviceLockTaken)
                        {
                            Monitor.Exit(_deviceLock);
                        }
                    }
                    
                    // Process devices outside the lock to minimize lock contention
                    foreach (var d in devices)
                    {
                        try
                        {
                            presentDevices.Add(d.ID);
                            if (deviceMap.TryGetValue(d.ID, out var device))
                            {
                                device.Update(d);
                            }
                            else
                            {
                                devicesToAdd.Add(new AudioDeviceInfo(d));
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error processing device {d.ID}");
                        }
                        finally
                        {
                            d.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error enumerating devices");
                    _consecutiveRefreshErrors++;
                    return;
                }

                if (presentDevices.Count == 0)
                {
                    Logger.Warn("No Active Audio Devices Found");
                }

                // Find devices to remove
                for (int i = 0; i < Devices.Count; i++)
                {
                    if (!presentDevices.Contains(Devices[i].ID))
                    {
                        indexesToRemove.Add(i);
                    }
                }

                // Remove devices in reverse order to avoid index issues
                for (int i = indexesToRemove.Count - 1; i >= 0; i--)
                {
                    Devices.RemoveAt(indexesToRemove[i]);
                }

                // Add new devices
                foreach (var device in devicesToAdd)
                {
                    Devices.Add(device);
                }

                // Success - reset error counter
                _consecutiveRefreshErrors = 0;
                
                // Notify listeners
                DevicesChanged?.Invoke(this, EventArgs.Empty);
                
                if (defaultChanged)
                {
                    DefaultDeviceChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error refreshing devices");
                _consecutiveRefreshErrors++;
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(_deviceRefreshLock);
                }
            }
        }

        private string GetDefaultDevice()
        {
            if (_isDisposed)
                return null;

            Logger.Debug("Getting Default Audio Device");
            try
            {
                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(_deviceLock, 500, ref lockTaken);
                    
                    if (!lockTaken)
                    {
                        Logger.Warn("Could not acquire device lock for GetDefaultDevice - timeout exceeded");
                        return DefaultDevice; // Return existing default if we can't get a lock
                    }
                    
                    if (!_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    {
                        Logger.Warn("No Default Audio Render Endpoint");
                        return null;
                    }
                    
                    using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    {
                        if (device != null)
                        {
                            Logger.Debug($"Default Audio Device: {device.ID}");
                            return device.ID;
                        }
                        else
                        {
                            Logger.Warn("Default Audio Device is null");
                            return null;
                        }
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        Monitor.Exit(_deviceLock);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting default device");
                return null;
            }
        }
        
        /// <summary>
        /// Gets the friendly name of a device by its ID
        /// </summary>
        public string GetDeviceNameById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;
            
            var device = Devices.FirstOrDefault(d => d.ID == id);
            return device?.DisplayName ?? "Unknown Device";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                _isDisposed = true;
                
                try
                {
                    _deviceCheckTimer?.Dispose();
                    _deviceCheckTimer = null;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing device check timer");
                }
                
                try
                {
                    _enumerator?.UnregisterEndpointNotificationCallback(this);
                    _enumerator?.Dispose();
                    _enumerator = null;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing AudioDeviceSource");
                }
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (_isDisposed) return;
            Logger.Debug($"Device state changed: {deviceId}, new state: {newState}");
            Task.Delay(100).ContinueWith(_ => RefreshDevices()); // Delay slightly to avoid thrashing
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            if (_isDisposed) return;
            Logger.Debug($"Device added: {pwstrDeviceId}");
            Task.Delay(100).ContinueWith(_ => RefreshDevices());
        }

        public void OnDeviceRemoved(string deviceId)
        {
            if (_isDisposed) return;
            Logger.Debug($"Device removed: {deviceId}");
            Task.Delay(100).ContinueWith(_ => RefreshDevices());
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (_isDisposed) return;
            Logger.Debug($"Default device changed: {defaultDeviceId}, flow: {flow}, role: {role}");
            if (flow == DataFlow.Render && (role == Role.Multimedia || role == Role.Console))
            {
                Task.Delay(100).ContinueWith(_ => RefreshDevices());
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            if (_isDisposed) return;
            
            try
            {
                // This is where format changes are detected
                Logger.Debug($"Property value changed for device: {pwstrDeviceId} - {key.formatId}");
                
                // Force a device refresh to capture format changes
                RefreshDevices();
                
                // Explicitly notify that this device might have changed format
                DeviceFormatChanged?.Invoke(this, pwstrDeviceId);
            }
            catch (Exception ex) 
            {
                Logger.Error(ex, "Error in OnPropertyValueChanged");
            }
        }

        ~AudioDeviceSource()
        {
            Dispose(false);
        }
    }
}
