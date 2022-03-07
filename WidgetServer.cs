using FrontierWidgetFramework;
using System;
using System.Drawing;

namespace AudioVisualizerWidget {
    public partial class AudioVisualizerWidget : IWidgetServer {

        // Functionality
        public string ResourcePath;

        public ErrorCode Load(string resource_path) {
            this.ResourcePath = resource_path;
            widgetPreview2x2 = new Bitmap(ResourcePath + "2x2.png");
            widgetPreview3x2 = new Bitmap(ResourcePath + "3x2.png");
            widgetPreview4x2 = new Bitmap(ResourcePath + "4x2.png");
            widgetPreview4x3 = new Bitmap(ResourcePath + "4x3.png");
            widgetPreview5x1 = new Bitmap(ResourcePath + "5x1.png");
            widgetPreview5x2 = new Bitmap(ResourcePath + "5x2.png");



            // Register widget
            WidgetManager.RegisterWidget(this);
            return ErrorCode.NoError;
        }

        public ErrorCode Unload() {
            return ErrorCode.NoError;
        }

        public Bitmap GetWidgetPreview(WidgetSize widget_size) {
            switch (widget_size)
            {
                case WidgetSize.SIZE_2X2:
                    return widgetPreview2x2;
                case WidgetSize.SIZE_3X2:
                    return widgetPreview3x2;
                case WidgetSize.SIZE_4X2:
                    return widgetPreview4x2;
                case WidgetSize.SIZE_4X3:
                    return widgetPreview4x3;
                case WidgetSize.SIZE_5X1:
                    return widgetPreview5x1;
                case WidgetSize.SIZE_5X2:
                    return widgetPreview5x2;
            }

            return widgetPreview5x1;
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widget_size, Guid instance_guid) {
            WidgetInstance widget_instance = new WidgetInstance(this, widget_size, instance_guid);
            return widget_instance;
        }

        public bool RemoveWidgetInstance(Guid instance_guid) {
            throw new NotImplementedException();
        }

        // Class specific
        public Random random;
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
