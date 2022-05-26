using FrontierWidgetFramework;
using FrontierWidgetFramework.WidgetUtility;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace AudioVisualizerWidget {
    public partial class AudioVisualizerWidget : IWidgetObject {

        // Identity
        public Guid Guid {
            get {
                return new Guid(GetType().Assembly.GetName().Name);
            }
        }
        public string Name {
            get {
                return "Audio Visualizer";
            }
        }
        public string Description {
            get {
                return "An audio visualizer widget for the Frontier";
            }
        }
        public string Author {
            get {
                return "Defyworks";
            }
        }
        public string Website {
            get {
                return "https://defyworks.com/";
            }
        }
        public Version Version {
            get {
                return new Version(1,0,0);
            }
        }

        // Capabilities
        public SdkVersion TargetSdk {
            get {
                return SdkVersion.Version_0;
            }
        }

        public List<WidgetSize> SupportedSizes
        {
            get
            {
                return new List<WidgetSize>() {
                    new WidgetSize(2, 2),
                    new WidgetSize(3, 2),
                    new WidgetSize(4, 2),
                    new WidgetSize(4, 3),
                    new WidgetSize(5, 1),
                    new WidgetSize(5, 2),
                };
            }
        }

        public Bitmap PreviewImage
        {
            get
            {
                return new Bitmap(ResourcePath + "3x2.png");
            }
        }

        // Functionality
        public IWidgetManager WidgetManager { get; set; }

        // Error handling
        public string LastErrorMessage { get; set; }

    }
}
