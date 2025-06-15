using Newtonsoft.Json;
using SimHub.Plugins;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Timers;
using System.Windows.Media;

namespace KatePolak.GlucoseForSimHub {

    [PluginDescription("Connects to your smart glucose sensors and allows you to use the measured values anywhere within SimHub")]
    [PluginAuthor("Kate Polak")]
    [PluginName("Glucose connector")]
    public class GlucoseForSimHubPlugin : IPlugin, IWPFSettingsV2, INotifyPropertyChanged {

        public class SimHubGlucosePluginSettings : INotifyPropertyChanged {

            private float _placeholderValue;
            private int _freshValueCutoff;
            private string _selectedSourceID;

            [JsonProperty("placeholderValue")]
            [DefaultValue(0)]
            public float PlaceholderValue {
                get => _placeholderValue;
                set {
                    _placeholderValue = value;
                    OnPropertyChanged();
                }
            }

            [JsonProperty("freshValueCutoff")]
            [DefaultValue(300)]
            public int FreshValueCutoff {
                get => _freshValueCutoff;
                set {
                    _freshValueCutoff = value;
                    OnPropertyChanged();
                }
            }

            [JsonProperty("selectedSourceId")]
            [DefaultValue("")]
            public string SelectedSourceID {
                get => _selectedSourceID;
                set {
                    _selectedSourceID = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string name = null) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        private SimHubGlucosePluginSettings _settings;
        private GlucoseSource _activeSource;
        private Timer _timer;

        private float _currentValue;
        private DateTimeOffset _valueTimestamp;

        /// <summary>
        /// Instance of the current plugin manager
        /// </summary>
        public PluginManager PluginManager { get; set; }

        /// <summary>
        /// Gets the left menu icon. Icon must be 24x24 and compatible with black and white display.
        /// </summary>
        public ImageSource PictureIcon => this.ToIcon(Properties.Resources.sdkmenuicon);

        /// <summary>
        /// Gets a short plugin title to show in left menu. Return null if you want to use the title as defined in PluginName attribute.
        /// </summary>
        public string LeftMenuTitle => "Glucose connector";

        public SimHubGlucosePluginSettings Settings => _settings;

        public GlucoseSource ActiveSource {
            get => _activeSource;
            protected set {
                _activeSource = value;
                OnPropertyChanged();
            }
        }

        public float? CurrentValue {
            get => _currentValue;
            set {
                _currentValue = value ?? Settings.PlaceholderValue;

                this.TriggerEvent("GlucoseUpdated");

                // Setting the timer interval restarts it automatically,
                // it's a bit counterunituitive but the cleanest way to do it
                _timer.Interval = Settings.FreshValueCutoff * 1000;

                OnPropertyChanged();
            }
        }

        public DateTimeOffset ValueTimestamp {
            get => _valueTimestamp;
            set {
                _valueTimestamp = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// Called at plugin manager stop, close/dispose anything needed here !
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void End(PluginManager pluginManager) {
            // Save settings
            WriteSettings("GeneralSettings", _settings);
            ActiveSource?.End();
        }

        /// <summary>
        /// Returns the settings control, return null if no settings control is required
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <returns></returns>
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager) {
            return new SettingsControl(this);
        }

        /// <summary>
        /// Called once after plugins startup
        /// Plugins are rebuilt at game change
        /// </summary>
        /// <param name="pluginManager"></param>
        public void Init(PluginManager pluginManager) {
            SimHub.Logging.Current.Info("Starting plugin");

            // Load settings
            _settings = ReadSettings("GeneralSettings", () => new SimHubGlucosePluginSettings());

            // Listen to changes to the selected source ID and reinitialize the source,
            _settings.PropertyChanged += (_, prop) => {
                switch (prop.PropertyName) {
                    case nameof(_settings.SelectedSourceID):
                        InitSource(_settings.SelectedSourceID);
                        break;
                    case nameof(_settings.FreshValueCutoff):
                        _timer.Interval = _settings.FreshValueCutoff * 1000;
                        break;
                }
            };

            // Initial source initialization
            if (_settings.SelectedSourceID != "" && _settings.SelectedSourceID != null) {
                InitSource(_settings.SelectedSourceID);
            }

            // Setup the fresh value timer, this gets reset every time a new
            // valid value is set, meaning if it expires there hasn't been a
            // new one for the configured duration and the existing value gets
            // replaced with a placeholder
            _timer = new Timer(_settings.FreshValueCutoff * 1000);
            _timer.Elapsed += (_, __) => {
                CurrentValue = _settings.PlaceholderValue;
                ValueTimestamp = DateTimeOffset.Now;
            };
            _timer.AutoReset = true;
            _timer.Enabled = true;
            _timer.Start();

            this.AttachDelegate("GlucoseLevel", () => CurrentValue);
            this.AttachDelegate("GlucoseLevelTimestamp", () => ValueTimestamp);
            this.AttachDelegate("GlucoseSourceStatus", () => ActiveSource.Status);

            this.AddEvent("GlucoseUpdated");

            this.AddAction("GlucoseForceUpdate", (_, __) => ActiveSource.ForceUpdate());
        }

        /// <summary>
        /// Gracefully shuts down the existing source, if any, and initializes a new one
        /// </summary>
        /// <param name="id">ID of the source type to initialize <see cref="GlucoseSource.SourcesByID"/></param>
        public void InitSource(string id) {
            ActiveSource?.End();

            ActiveSource = GlucoseSource.SourcesByID[id].Factory(this);
            ActiveSource.ValueUpdated += (_, update) => {
                var (value, timestamp) = update;

                var valueAge = (DateTimeOffset.UtcNow - timestamp).TotalSeconds;

                // If the value provided by the source is older than the configured
                // "freshness" cutoff, we instead use the configured placeholder
                // with the current timestamp marking it as "fresh"
                if (valueAge < _settings.FreshValueCutoff) {
                    CurrentValue = value;
                    ValueTimestamp = timestamp.LocalDateTime;
                } else {
                    CurrentValue = Settings.PlaceholderValue;
                    ValueTimestamp = DateTimeOffset.Now;
                }
            };
        }

        public T ReadSettings<T>(string settingsName, Func<T> defaultValueFactory) {
            return this.ReadCommonSettings<T>(settingsName, defaultValueFactory);
        }

        public void WriteSettings<T>(string settingsName, T value) {
            this.SaveCommonSettings<T>(settingsName, value);
        }
    }
}
