using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WoteverCommon;
using System.Timers;
using System.Globalization;

namespace KatePolak.GlucoseForSimHub {

    /// <summary>
    /// 
    /// References used to implement this source:
    /// https://marwan.craft.me/1kkHHnWW0lddpv
    /// https://gist.github.com/khskekec/6c13ba01b10d3018d816706a32ae8ab2
    /// </summary>
    public class LibreLinkUpAPI : GlucoseSource {

        /// <summary>
        /// Shape of the settings file of the source, json property names are obscure on purpose,
        /// since we are storing the email and password the least we can do is call the field
        /// "e" instead of "email" to make the job of any general purpose info stealer harder,
        /// the actual values are "encrypted" by XORing them for the same purpose
        /// </summary>
        public class LLUSettings {

            [JsonProperty("e")]
            public string Email { get; set; }

            [JsonProperty("p")]
            public string Password { get; set; }

            [JsonProperty("u")]
            public string HashedUserID { get; set; }

            [JsonProperty("t")]
            public string Token { get; set; }

            [JsonProperty("pi")]
            public string PatientId { get; set; }

            [JsonProperty("pn")]
            public string PatientName { get; set; }

            [JsonProperty("r")]
            public string Region { get; set; }

            [JsonProperty("te")]
            public int TokenExpires { get; set; }

            [JsonProperty("interval")]
            public int Interval { get; set; }
        }

        public struct Patient {

            [JsonProperty("patientId")]
            public string PatientId;

            [JsonProperty("firstName")]
            public string FirstName;

            [JsonProperty("lastName")]
            public string LastName;

            public override string ToString() {
                return string.Join(" ", FirstName, LastName);
            }
        }

        private struct LoginStruct {
            [JsonProperty("email")]
            public string Email;

            [JsonProperty("password")]
            public string Password;
        }

        private HttpClient _httpClient;

        private readonly LLUSettings _settings;

        private Timer _timer;

        /// <summary>
        /// XORs a string one character at a time,
        /// used to obscure the actual values when stored in the settings file
        /// </summary>
        /// <param name="s">The string to XOR</param>
        /// <returns>The XORed string</returns>
        private string XOR(string s) {
            return string.Join("", s.Select(c => (char)(c ^ 0x42)));
        }

        /// <summary>
        /// XORs a number,
        /// used to obscure the actual values when stored in the settings file
        /// </summary>
        /// <param name="n">The number to XOR</param>
        /// <returns>The XORed number</returns>
        private int XOR(int n) {
            return n ^ 0x42424242;
        }

        public LibreLinkUpAPI(GlucoseForSimHubPlugin plugin) : base(plugin) {

            var settings = _plugin.ReadSettings("LLUSettings", () => new LLUSettings());

            // We need to "decrypt" the values before actually using them,
            // just a simple layer of minimal safety, anything but bulletproof but better than nothing
            _settings = new LLUSettings {
                Email        = XOR(settings.Email        ?? ""),
                Password     = XOR(settings.Password     ?? ""),
                HashedUserID = XOR(settings.HashedUserID ?? ""),
                Token        = XOR(settings.Token        ?? ""),
                PatientId    = XOR(settings.PatientId    ?? ""),
                PatientName  = XOR(settings.PatientName  ?? ""),
                Region       = XOR(settings.Region       ?? ""),
                TokenExpires = XOR(settings.TokenExpires),
                Interval     = settings.Interval,
            };

            // A somewhat hacky way to set the default interval to 1 minute
            if (_settings.Interval == 0 || _settings.Interval < 0) _settings.Interval = 60;

            if (_settings.Token == "") {
                Status = "Not logged in";
            } else if (_settings.Token != "" && _settings.PatientId == "") {
                Status = "Logged in, no patient selected";
            } else {
                Status = $"Logged in, active patient: {_settings.PatientName}";
            }

            // Log in again if the existing token will be valid for less than 7 days
            if ((DateTimeOffset.FromUnixTimeSeconds(_settings.TokenExpires) - DateTimeOffset.Now).Days < 7) {
                SimHub.Logging.Current.Info($"LLU API token expires in less than 7 days, refreshing the login");
                Task.Run(async () => {
                    await Login(_settings.Email, _settings.Password);
                });
            }

            // EU is used as an arbitrary default, the API will redirect us to the correct region
            if (_settings.Region == "") _settings.Region = "eu";

            UpdateClient();
            UpdateTimer();

            ForceUpdate();
        }

        private void UpdateTimer() {
            _timer = new Timer(_settings.Interval * 1000);
            _timer.Elapsed += (s, e) => ForceUpdate();
            _timer.AutoReset = true;
            _timer.Enabled = true;
        }

        public override void End() {
            _plugin.WriteSettings("LLUSettings", new LLUSettings {
                Email        = XOR(_settings.Email),
                Password     = XOR(_settings.Password),
                HashedUserID = XOR(_settings.HashedUserID),
                Token        = XOR(_settings.Token),
                PatientId    = XOR(_settings.PatientId),
                PatientName  = XOR(_settings.PatientName),
                Region       = XOR(_settings.Region),
                TokenExpires = XOR(_settings.TokenExpires),
                Interval     = _settings.Interval
            });
        }

        public async override void ForceUpdate() {
            try {
                var (value, timestamp) = await GetLatestMeasurement();

                if (timestamp == null) return;

                OnValueUpdated(value, (DateTimeOffset) timestamp);
            } catch (Exception ex) {
                SimHub.Logging.Current.Error(ex);
                OnValueUpdated(null, DateTimeOffset.Now);
            }
        }

        public override void OpenSettingsWindow() {
            new LLULoginWindow(this).Show();
        }

        /// <summary>
        /// Sets <see cref="_httpClient"/> up, base address is set and all headers and authorization headers are set up and ready for use, if logged in
        /// </summary>
        private void UpdateClient() {
            _httpClient = new HttpClient(new HttpClientHandler {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
            });
            _httpClient.DefaultRequestHeaders.Add("accept-encoding", "gzip");
            _httpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");
            _httpClient.DefaultRequestHeaders.Add("connection", "Keep-Alive");
            _httpClient.DefaultRequestHeaders.Add("product", "llu.ios");
            _httpClient.DefaultRequestHeaders.Add("version", "4.12.0");
            _httpClient.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; rv:129.0) Gecko/20100101 Firefox/129.0");

            if (_settings.Token  != "") _httpClient.DefaultRequestHeaders.Add("authorization", $"Bearer {_settings.Token}");
            if (_settings.HashedUserID != "") _httpClient.DefaultRequestHeaders.Add("Account-Id", _settings.HashedUserID);

            _httpClient.BaseAddress = new Uri($"https://api-{_settings.Region}.libreview.io");
        }

        /// <summary>
        /// Checks if the response is a redirect to a different LLU region,
        /// and changes the settings accordingly
        /// </summary>
        /// <param name="response">The entire reponse from the LLU API</param>
        /// <returns>True if the response was a redirect and the request has to be redone to get the true reponse</returns>
        private bool CheckRedirect(string response) {
            var res = JObject.Parse(response);

            var redirect = res.SelectToken("data.redirect");

            // Assume that both redirect == false and it not existing at all means the same thing
            if (redirect == null || ((bool)redirect) == false) return false;

            var region = (string) res.SelectToken("data.region");
            SimHub.Logging.Current.Info($"Incorrect LLU region, updating to '{region}' and retrying request");
            _settings.Region = region;
            UpdateClient();

            return true;
        }

        public async Task<(bool, string)> Login(string email, string password) {

            // This loop is here to handle redirects between regions,
            // basically a form of "go-to the start"
            while (true) {
                SimHub.Logging.Current.Info("Logging into LLU");

                var content = new StringContent(new LoginStruct { Email = email, Password = password }.ToJson(), Encoding.UTF8, "application/json");
                var res = await _httpClient.PostAsync("/llu/auth/login", content);
                var responseString = await res.Content.ReadAsStringAsync();

                // CheckRedirect will return true if the region didn't match
                // and we need to redo the request, so we go back to the start of the while loop
                if (CheckRedirect(responseString)) continue;

                var resObject = JObject.Parse(responseString);

                var status = (int) resObject.SelectToken("status");

                if (status != 0) return (false, (string)resObject.SelectToken("error.message"));

                _settings.Email    = email;
                _settings.Password = password;

                // The API expects the user ID hashed with SHA256, so we store it like that locally,
                // the less private info we store in plain text the better
                var userIdBytes = Encoding.UTF8.GetBytes((string) resObject.SelectToken("data.user.id"));
                var userIdHash  = SHA256.Create().ComputeHash(userIdBytes);
                _settings.HashedUserID = string.Join("", userIdHash.Select(b => b.ToString("x2")));

                _settings.Token        = (string)resObject.SelectToken("data.authTicket.token");
                _settings.TokenExpires =    (int)resObject.SelectToken("data.authTicket.expires");
                _settings.PatientId    = "";
                _settings.PatientName  = "";

                SimHub.Logging.Current.Info("LLU login successful");

                Status = "Logged in, no patient selected";

                UpdateClient();

                return (true, "Login successful");
            }
        }

        public bool IsLoggedIn() {
            return _settings.Token != "";
        }

        public async Task<Patient[]> GetPatients() {
            if (_settings.Token == "" || _settings.HashedUserID == "") return new Patient[0];

            SimHub.Logging.Current.Info("Querying LLU patients");

            while (true) {

                var res = await _httpClient.GetAsync("/llu/connections");
                var responseString = await res.Content.ReadAsStringAsync();

                // CheckRedirect will return true if the region didn't match
                // and we need to redo the request, so we go back to the start of the while loop
                if (CheckRedirect(responseString)) continue;

                var resObject = JObject.Parse(responseString);

                var status = (int) resObject.SelectToken("status");

                if (status != 0) return new Patient[0];

                return resObject.SelectToken("data").ToObject<Patient[]>();
            }
        }

        public void SetActivePatient(Patient p) {
            _settings.PatientId = p.PatientId;
            _settings.PatientName = p.ToString();

            Status = $"Logged in, active patient: {_settings.PatientName}";
        }

        public string GetActivePatientName() {
            return _settings.PatientName;
        }

        private async Task<(float?, DateTimeOffset?)> GetLatestMeasurement() {
            if (_settings.Token == "" || _settings.HashedUserID == "" || _settings.PatientId == "") return (null, null);

            SimHub.Logging.Current.Info("Getting latest LLU measurement");

            // The LLU API has a redirect feature for different regions,
            // in theory we should only really encounter this during the login,
            // but we handle it during all request just to be sure
            while (true) {

                var res = await _httpClient.GetAsync($"/llu/connections/{_settings.PatientId}/graph");
                var responseString = await res.Content.ReadAsStringAsync();

                // We have seen JObject.Parse complain about null values,
                // this try catch should help show where that null value
                // actually comes from as the raw stack traces are pretty useless
                //
                // FIXME: Likely remove this once the issue is identified and fixed
                try {

                    // CheckRedirect will return true if the region didn't match
                    // and we need to redo the request, so we go back to the start of the while loop
                    if (CheckRedirect(responseString)) continue;
                
                    var resObject = JObject.Parse(responseString);

                    var status = (int) resObject.SelectToken("status");

                    if (status != 0) return (null, null);

                    return (
                        (float)resObject.SelectToken("data.connection.glucoseMeasurement.Value"),
                        DateTimeOffset.ParseExact((string)resObject.SelectToken("data.connection.glucoseMeasurement.FactoryTimestamp") + " +0:00", "M/d/yyyy h:mm:ss tt zzz", CultureInfo.InvariantCulture)
                    );
                } catch (Exception ex) {
                    SimHub.Logging.Current.Debug(responseString);
                    SimHub.Logging.Current.Error(ex);

                    return (null, null);
                }
            }
        }
    }
}
