﻿using FrontierWidgetFramework;
using FrontierWidgetFramework.WidgetUtility;
using System;
using System.Collections.Generic;

namespace AudioVisualizerWidget {
    public partial class AudioVisualizerWidget : IWidgetObject {

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
        public Version Version {
            get {
                return new Version(1,0,0);
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

        // Functionality
        public IWidgetManager WidgetManager { get; set; }

        // Error handling
        public string LastErrorMessage { get; set; }

    }
}
