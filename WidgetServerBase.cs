using FrontierWidgetFramework;
using System;
using System.Collections.Generic;

namespace AudioVisualizerWidget {
    public partial class AudioVisualizerWidget : IWidgetServer {

        // Identity
        public Guid Guid {
            get {
                return new Guid("36dd71ba-fa7a-496b-82be-db664c1bc087");
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
        public int Version {
            get {
                return 100;
            }
        }
        public string VersionString {
            get {
                return "1.0.0";
            }
        }

        // Capabilities
        public SdkVersion TargetSdk {
            get {
                return SdkVersion.Version_0;
            }
        }
        public List<WidgetSize> SupportedSizes {
            get {
                return new List<WidgetSize>() {
                    WidgetSize.SIZE_2X2,
                    WidgetSize.SIZE_3X2,
                    WidgetSize.SIZE_4X2,
                    WidgetSize.SIZE_4X3,
                    WidgetSize.SIZE_5X1,
                    WidgetSize.SIZE_5X2,
                };
            }
        }

        // Functionality
        public IWidgetManager WidgetManager { get; set; }

        // Error handling
        public string LastErrorMessage { get; set; }

    }
}
