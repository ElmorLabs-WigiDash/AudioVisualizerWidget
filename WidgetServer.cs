using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Drawing;
using System.IO;

namespace AudioVisualizerWidget {
    public partial class AudioVisualizerWidget : IWidgetObject
    {
        // Functionality
        public string ResourcePath;

        public WidgetError Load(string resource_path) {
            this.ResourcePath = resource_path;
            thumb = new Bitmap(Path.Combine(ResourcePath, "thumb.png"));
            widgetPreview2x2 = new Bitmap(ResourcePath + "2x2.png");
            widgetPreview3x2 = new Bitmap(ResourcePath + "3x2.png");
            widgetPreview4x2 = new Bitmap(ResourcePath + "4x2.png");
            widgetPreview4x3 = new Bitmap(ResourcePath + "4x3.png");
            widgetPreview5x1 = new Bitmap(ResourcePath + "5x1.png");
            widgetPreview5x2 = new Bitmap(ResourcePath + "5x2.png");

            return WidgetError.NO_ERROR;
        }

        public WidgetError Unload() {
            return WidgetError.NO_ERROR;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size) {
            if (widget_size.Equals(2, 2)) return widgetPreview2x2;
            if (widget_size.Equals(3, 2)) return widgetPreview3x2;
            if (widget_size.Equals(4, 2)) return widgetPreview4x2;
            if (widget_size.Equals(4, 3)) return widgetPreview4x3;
            if (widget_size.Equals(5, 1)) return widgetPreview5x1;
            if (widget_size.Equals(5, 2)) return widgetPreview5x2;

            return widgetPreview5x1;
        }

        public Bitmap WidgetThumbnail => thumb;

        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid) {
            WidgetInstance widget_instance = new WidgetInstance(this, widget_size, instance_guid);
            return widget_instance;
        }

        public bool RemoveWidgetInstance(Guid instance_guid) {
            throw new NotImplementedException();
        }

        // Class specific
        public Random random;
        private Bitmap thumb;
        private Bitmap widgetPreview2x2;
        private Bitmap widgetPreview3x2;
        private Bitmap widgetPreview4x2;
        private Bitmap widgetPreview4x3;
        private Bitmap widgetPreview5x1;
        private Bitmap widgetPreview5x2;

        public AudioVisualizerWidget() {
            
            random = new Random();

        }
    }

}
