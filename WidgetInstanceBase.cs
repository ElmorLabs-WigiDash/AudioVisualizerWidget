using FrontierWidgetFramework;
using System;
using System.Drawing;

namespace AudioVisualizerWidget {

    public partial class WidgetInstance : IWidgetInstance {

        // Identity
        private AudioVisualizerWidget parent;
        public IWidgetServer Parent { 
            get { 
                return parent;
            }
        }
        public Guid Guid { get; set; }

        // Size
        public Size Size { get; set; }

        // Capabilities
        public bool HasSettings {
            get {
                return true;
            }
        }

        public event WidgetUpdatedEventHandler WidgetUpdated;

        /*protected virtual void OnWidgetUpdated(WidgetUpdatedEventArgs e) {
            WidgetUpdatedEventHandler handler = WidgetUpdated;
            handler?.Invoke(this, e);
        }*/

    }
}
