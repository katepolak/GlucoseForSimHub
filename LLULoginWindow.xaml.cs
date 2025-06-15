using SimHub.Plugins.UI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using WoteverCommon.Extensions;

namespace KatePolak.GlucoseForSimHub {
    public partial class LLULoginWindow : SHMetroWindow {

        private readonly LibreLinkUpAPI _lluAPI;

        public ObservableCollection<LibreLinkUpAPI.Patient> Patients { get; set; }

        public LLULoginWindow(LibreLinkUpAPI lluAPI) {
            InitializeComponent();
            DataContext = this;
            _lluAPI = lluAPI;
            Patients = new ObservableCollection<LibreLinkUpAPI.Patient>();

            if (_lluAPI.IsLoggedIn()) {
                Dispatcher.BeginInvoke(new Action(async () => {
                    Patients.Clear();
                    Patients.AddAll(await _lluAPI.GetPatients());
                    patientSelect.SelectedItem = Patients.First(p => p.ToString() == _lluAPI.GetActivePatientName());
                    patientSelect.IsEnabled = true;
                }));
            }
        }

        private async void LoginClick(object sender, RoutedEventArgs e) {
            var (loggedIn, message) = await _lluAPI.Login(emailBox.Text, passwordBox.Password);

            resultLabel.Content = message;

            if (loggedIn) {
                Patients.Clear();
                Patients.AddAll(await _lluAPI.GetPatients());
                patientSelect.IsEnabled = true;
            }
        }

        private void PatientSelect(object sender, RoutedEventArgs e) {
            var index = patientSelect.SelectedIndex;

            if (index == -1) return;

            _lluAPI.SetActivePatient(Patients[patientSelect.SelectedIndex]);

            this.Close();
        }
    }
}
