using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace KatePolak.GlucoseForSimHub {

    public abstract class GlucoseSource : INotifyPropertyChanged {

        #region Factory

        /// <summary>
        /// Holds information about source implementations used for the factory pattern
        /// </summary>
        public struct GlucoseSourceInfo {
            public string ID;
            public string DisplayName;
            public Func<GlucoseForSimHubPlugin, GlucoseSource> Factory;

            public override string ToString() {
                return DisplayName;
            }
        }

        /// <summary>
        /// List of all available sources, done manually instead of reflections for simplicity
        /// </summary>
        public static IReadOnlyCollection<GlucoseSourceInfo> Sources = new List<GlucoseSourceInfo> {
            new GlucoseSourceInfo {
                ID          = "llu",
                DisplayName = "Libre Link Up",
                Factory     = (plugin) => new LibreLinkUpAPI(plugin)
            }
        }.AsReadOnly();

        /// <summary>
        /// Alternative indexing of <see cref="Sources"/>
        /// </summary>
        public static IReadOnlyDictionary<string, GlucoseSourceInfo> SourcesByID = Sources.ToDictionary(s => s.ID, s => s);

        #endregion

        private string _status;
        protected GlucoseForSimHubPlugin _plugin;

        /// <summary>
        /// An arbitrary user friendly source status description
        /// </summary>
        public string Status {
            get => _status;
            set {
                _status = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Initialize the source
        /// </summary>
        /// <param name="plugin">Primarily used to access <see cref="GlucoseForSimHubPlugin.ReadSettings{T}(string, Func{T})"/> and <see cref="GlucoseForSimHubPlugin.WriteSettings{T}(string, T)"/></param>
        public GlucoseSource(GlucoseForSimHubPlugin plugin) {
            _plugin = plugin;
        }

        /// <summary>
        /// Called before the entire plugin is unloaded, should save all settings using <see cref="GlucoseForSimHubPlugin.WriteSettings{T}(string, T)"/>
        /// </summary>
        public abstract void End();

        /// <summary>
        /// Used to force an update of the value outside any timers the source uses internally
        /// </summary>
        public abstract void ForceUpdate();

        /// <summary>
        /// Should open the settings window for this source
        /// </summary>
        public abstract void OpenSettingsWindow();

        /// <summary>
        /// Called when a new value is available, null means the update failed
        /// </summary>
        public event EventHandler<(float?, DateTimeOffset)> ValueUpdated;

        /// <summary>
        /// Call when a new value is available
        /// </summary>
        /// <param name="newValue">The value, null if the update failed</param>
        /// <param name="timestamp">When the value was measured</param>
        protected void OnValueUpdated(float? newValue, DateTimeOffset timestamp) {
            ValueUpdated?.Invoke(this, (newValue, timestamp));
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
