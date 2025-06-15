using SimHub.Plugins.Styles;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace KatePolak.GlucoseForSimHub {

    public partial class SettingsControl : UserControl {

        public GlucoseForSimHubPlugin Plugin { get; }

        public SettingsControl() {
            InitializeComponent();
            DataContext = this;
        }

        public SettingsControl(GlucoseForSimHubPlugin plugin) : this() {
            Plugin = plugin;

            SourceComboBox.ItemsSource = GlucoseSource.Sources;
            if (Plugin.Settings.SelectedSourceID != null) {
                SourceComboBox.SelectedItem = GlucoseSource.SourcesByID[Plugin.Settings.SelectedSourceID];
            }
        }

        private void OpenSettingsWindow(object sender, System.Windows.RoutedEventArgs e) {
            Plugin?.ActiveSource?.OpenSettingsWindow();
        }

        private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            Plugin.Settings.SelectedSourceID = ((GlucoseSource.GlucoseSourceInfo) SourceComboBox.SelectedItem).ID;
        }

        private void CheckIfNumber(object sender, System.Windows.Input.TextCompositionEventArgs e) {
            e.Handled = new Regex("[^0-9]+").IsMatch(e.Text);
        }
    }
}
