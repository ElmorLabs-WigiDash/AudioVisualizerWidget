using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Drawing;

namespace AudioVisualizerWidget {

    public partial class WidgetInstance : IWidgetInstance {

        // Identity
        private AudioVisualizerWidget parent;
        public IWidgetObject WidgetObject
        { 
            get { 
                return parent;
            }
        }
        public Guid Guid { get; set; }

        // Size
        public WidgetSize WidgetSize { get; set; }

        // Events
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public void Dispose()
        {
            
        }
    }
}
