﻿using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace AudioVisualizerWidget {
    public partial class AudioVisualizerWidgetServer : IWidgetObject {

        // Identity
        public Guid Guid {
            get {
                return new Guid(GetType().Assembly.GetName().Name);
            }
        }
        public string Name {
            get {
                return AudioVisualizerWidget.Properties.Resources.Name_AudioVisualizer;
            }
        }
        public string Description {
            get {
                return AudioVisualizerWidget.Properties.Resources.Description_AnAudioVisualizer;
            }
        }
        public string Author {
            get {
                return "ElmorLabs";
            }
        }
        public string Website {
            get {
                return "https://elmorlabs.com/";
            }
        }
        public Version Version {
            get {
                return new Version(1,0,5);
            }
        }

        // Capabilities
        public SdkVersion TargetSdk {
            get {
                return WidgetUtility.CurrentSdkVersion;
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
