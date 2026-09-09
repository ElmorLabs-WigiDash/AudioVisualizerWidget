using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;

namespace AudioVisualizerWidget {
    public partial class AudioVisualizerWidgetServer : IWidgetObject, IDisposable
    {
        // Functionality
        public string ResourcePath;

        // Class specific
        public Random random;
        private Bitmap thumb;
        private Bitmap widgetPreview2x2;
        private Bitmap widgetPreview3x2;
        private Bitmap widgetPreview4x2;
        private Bitmap widgetPreview4x3;
        private Bitmap widgetPreview5x1;
        private Bitmap widgetPreview5x2;
        private bool _isDisposed;
        private readonly Dictionary<string, IWidgetInstance> _widgetInstances = new Dictionary<string, IWidgetInstance>();

        public AudioVisualizerWidgetServer() 
        {
            random = new Random();
        }

        public WidgetError Load(string resource_path) 
        {
            try
            {
                this.ResourcePath = resource_path;
                LoadBitmaps();
                return WidgetError.NO_ERROR;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Error loading resources: {ex.Message}";
                return WidgetError.UNDEFINED_ERROR;
            }
        }

        private void LoadBitmaps()
        {
            // Dispose any existing bitmaps first
            DisposeBitmaps();

            // Load new bitmaps
            thumb = new Bitmap(Path.Combine(ResourcePath, "thumb.png"));
            widgetPreview2x2 = new Bitmap(Path.Combine(ResourcePath, "2x2.png"));
            widgetPreview3x2 = new Bitmap(Path.Combine(ResourcePath, "3x2.png"));
            widgetPreview4x2 = new Bitmap(Path.Combine(ResourcePath, "4x2.png"));
            widgetPreview4x3 = new Bitmap(Path.Combine(ResourcePath, "4x3.png"));
            widgetPreview5x1 = new Bitmap(Path.Combine(ResourcePath, "5x1.png"));
            widgetPreview5x2 = new Bitmap(Path.Combine(ResourcePath, "5x2.png"));
        }

        public WidgetError Unload() 
        {
            try
            {
                DisposeBitmaps();
                return WidgetError.NO_ERROR;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Error unloading resources: {ex.Message}";
                return WidgetError.UNDEFINED_ERROR;
            }
        }

        private void DisposeBitmaps()
        {
            thumb?.Dispose();
            widgetPreview2x2?.Dispose();
            widgetPreview3x2?.Dispose();
            widgetPreview4x2?.Dispose();
            widgetPreview4x3?.Dispose();
            widgetPreview5x1?.Dispose();
            widgetPreview5x2?.Dispose();
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size) 
        {
            if (widget_size.Equals(2, 2)) return widgetPreview2x2;
            if (widget_size.Equals(3, 2)) return widgetPreview3x2;
            if (widget_size.Equals(4, 2)) return widgetPreview4x2;
            if (widget_size.Equals(4, 3)) return widgetPreview4x3;
            if (widget_size.Equals(5, 1)) return widgetPreview5x1;
            if (widget_size.Equals(5, 2)) return widgetPreview5x2;

            return widgetPreview5x1;
        }

        public Bitmap WidgetThumbnail => thumb;

        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid) 
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(AudioVisualizerWidgetServer));
                
            WidgetInstance widget_instance = new WidgetInstance(this, widget_size, instance_guid);
            _widgetInstances[instance_guid.ToString()] = widget_instance;
            return widget_instance;
        }

        public bool RemoveWidgetInstance(Guid instance_guid) 
        {
            if (_widgetInstances.TryGetValue(instance_guid.ToString(), out var instance))
            {
                if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                return _widgetInstances.Remove(instance_guid.ToString());
            }
            return false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                // Dispose all widget instances
                foreach (var instance in _widgetInstances.Values)
                {
                    if (instance is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _widgetInstances.Clear();

                DisposeBitmaps();
            }

            _isDisposed = true;
        }

        ~AudioVisualizerWidgetServer()
        {
            Dispose(false);
        }
    }
}
