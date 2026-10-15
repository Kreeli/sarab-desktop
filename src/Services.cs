using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace SarabDesktop
{
    public static class Backend
    {
        public const string RegistryPath = @"SYSTEM\CurrentControlSet\Services\SarabGnss\Parameters";
        public const string SarabHash = "8D8614A9B44658D2A1C4440E829078B6FE61C9E8AB735D53AEDFEAE33E2AA0E4";
        public const string ProbeHash = BuildTimePins.ProbeHash;
        public static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
        public static string SarabPath => Path.Combine(BaseDirectory, "backend", "sarab.exe");
        public static string ProbePath => Path.Combine(BaseDirectory, "backend", "location-probe.exe");
        public static string HelperPath => Path.Combine(BaseDirectory, "SarabDesktop.Helper.exe");
        public static void VerifyFile(string file, string expected)
        {
            if (!File.Exists(file)) throw new FileNotFoundException(L.T("缺少程序文件：") + Path.GetFileName(file));
            using (var stream = File.OpenRead(file))
            using (var hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("程序文件校验失败：") + Path.GetFileName(file) + L.T("。请使用完整的发行目录。"));
            }
        }
        public static Task<DriverState> ReadStateAsync() => Task.Run(() =>
        {
            var state = new DriverState();
            using (var key = Registry.LocalMachine.OpenSubKey(RegistryPath))
            {
                state.Enabled = key != null && Convert.ToInt32(key.GetValue("Enabled", 0)) != 0;
                if (key != null)
                {
                    double lat, lon;
                    if (double.TryParse(Convert.ToString(key.GetValue("Latitude")), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) && double.TryParse(Convert.ToString(key.GetValue("Longitude")), NumberStyles.Float, CultureInfo.InvariantCulture, out lon) && CoordinateParser.Valid(lat, lon))
                        state.Configured = new LocationProfile { Name = L.T("驱动当前配置"), Latitude = lat, Longitude = lon, Altitude = ReadDouble(key, "Altitude", 0), Accuracy = ReadDouble(key, "Accuracy", 5), Drift = Convert.ToInt32(key.GetValue("DriftEnabled", 0)) != 0, DriftRadius = ReadDouble(key, "DriftRadius", 1.2) };
                }
            }
            using (var consent = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location"))
                state.LocationAllowed = string.Equals(Convert.ToString(consent?.GetValue("Value")), "Allow", StringComparison.OrdinalIgnoreCase);
            using (var boot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control"))
                state.TestSigning = Convert.ToString(boot?.GetValue("SystemStartOptions")).IndexOf("TESTSIGNING", StringComparison.OrdinalIgnoreCase) >= 0;
            using (var searcher = new ManagementObjectSearcher("SELECT Name,HardwareID,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Name LIKE '%Sarab%'"))
            using (var devices = searcher.Get())
            {
                foreach (ManagementObject device in devices)
                {
                    var ids = device["HardwareID"] as string[];
                    if (ids == null || !ids.Any(id => string.Equals(id, @"Root\SarabGnss", StringComparison.OrdinalIgnoreCase))) continue;
                    state.Installed = true;
                    state.DeviceName = Convert.ToString(device["Name"]);
                    state.ProblemCode = Convert.ToInt32(device["ConfigManagerErrorCode"]);
                    state.Healthy = state.ProblemCode == 0;
                    if (state.Healthy) break;
                }
            }
            return state;
        });
        static double ReadDouble(RegistryKey key, string name, double fallback)
        {
            double value;
            return double.TryParse(Convert.ToString(key.GetValue(name)), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
        public static async Task<ProbeResult> ProbeAsync()
        {
            VerifyFile(ProbePath, ProbeHash);
            string result = await RunAsync(ProbePath, "", 23000);
            var value = Json.From<ProbeResult>(result.Trim());
            if (value == null || !CoordinateParser.Valid(value.latitude, value.longitude) || double.IsNaN(value.accuracyMeters) || value.accuracyMeters < 0) throw new InvalidDataException(L.T("定位读取器返回了无效结果。"));
            value.CapturedAt = DateTime.Now;
            return value;
        }
        public static async Task<string> RunAsync(string path, string arguments, int timeout)
        {
            using (var process = new Process { StartInfo = new ProcessStartInfo(path, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(path) } })
            {
                if (!process.Start()) throw new InvalidOperationException(L.T("无法启动定位读取器。"));
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                bool exited = await Task.Run(() => process.WaitForExit(timeout));
                if (!exited) { try { process.Kill(); } catch { } throw new TimeoutException(L.T("定位读取超时，请检查 Windows 定位权限和驱动状态。")); }
                string stdout = await output, stderr = await error;
                if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? L.T("定位读取失败，代码 ") + process.ExitCode : stderr.Trim());
                return stdout;
            }
        }
        public static async Task ElevateAsync(string arguments)
        {
            if (!File.Exists(HelperPath)) throw new FileNotFoundException(L.T("缺少 SarabDesktop.Helper.exe，请保持发行目录完整。"));
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(HelperPath, "--language " + L.Language + " " + arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = BaseDirectory }))
                {
                    if (process == null) throw new InvalidOperationException(L.T("管理员操作未启动。"));
                    await Task.Run(() => process.WaitForExit());
                    if (process.ExitCode != 0) throw new InvalidOperationException(L.T("管理员操作未完成。请查看弹出的错误说明；当前位置未被确认为已更改。"));
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            { throw new OperationCanceledException(L.T("你取消了管理员授权，未执行本次操作。")); }
        }
        public static string ApplyArguments(LocationProfile target)
        {
            target.Validate();
            return "--apply " + string.Join(" ", new[] { target.Latitude, target.Longitude, target.Altitude, target.Accuracy, target.Drift ? 1 : 0, target.DriftRadius }.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
        }
        public static string SarabSetArguments(LocationProfile target)
        {
            target.Validate();
            return "set --lat=" + target.Latitude.ToString("R", CultureInfo.InvariantCulture) + " --lon=" + target.Longitude.ToString("R", CultureInfo.InvariantCulture) + " --alt=" + target.Altitude.ToString("R", CultureInfo.InvariantCulture) + " --acc=" + target.Accuracy.ToString("R", CultureInfo.InvariantCulture) + " --drift=" + (target.Drift ? "true" : "false") + " --drift-radius=" + target.DriftRadius.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    public static class StartupRegistration
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "SarabDesktop";
        public static bool Enabled
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return string.Equals(Convert.ToString(key?.GetValue(Name)), Command, StringComparison.OrdinalIgnoreCase); }
        }
        public static string Command => "\"" + Assembly.GetEntryAssembly().Location + "\" --startup --tray";
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue(Name, Command, RegistryValueKind.String);
                else
                {
                    string existing = Convert.ToString(key.GetValue(Name));
                    if (!string.IsNullOrEmpty(existing) && !string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.T("同名启动项指向其他路径，已保留它。请检查 Windows 启动应用。"));
                    key.DeleteValue(Name, false);
                }
            }
        }
        public static void TestTemporaryRegistration()
        {
            string name = "SarabDesktop.SelfTest." + Process.GetCurrentProcess().Id;
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (key.GetValue(name) != null) throw new InvalidOperationException(L.T("测试启动项已存在。"));
                try { key.SetValue(name, Command); if ((string)key.GetValue(name) != Command) throw new InvalidDataException(L.T("启动项写入不一致。")); }
                finally { key.DeleteValue(name, false); }
            }
        }
    }

    public sealed class GeocodingService : IDisposable
    {
        readonly HttpClient client;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        DateTime lastOsmRequest = DateTime.MinValue;
        readonly Dictionary<string, List<SearchResult>> osmCache = new Dictionary<string, List<SearchResult>>();
        public GeocodingService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromSeconds(18) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SarabDesktop/1.0 (Windows; manual address lookup)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,ja;q=0.8,en;q=0.6");
        }
        public async Task<List<SearchResult>> SearchAsync(string text, string provider, string googleKey)
        {
            text = (text ?? "").Trim();
            if (text.Length < 2 || text.Length > 1000) throw new ArgumentException(L.T("请输入 2～1000 个字符的地址、坐标或 Google Maps 链接。"));
            LocationProfile parsed;
            if (CoordinateParser.TryParse(text, out parsed)) return new List<SearchResult> { new SearchResult { Title = parsed.Source, Detail = parsed.Coordinates, Location = parsed } };
            Uri url;
            if (Uri.TryCreate(text, UriKind.Absolute, out url) && (url.Scheme == "http" || url.Scheme == "https"))
            {
                if (url.Scheme != "https" || !CoordinateParser.IsGoogleHost(url.Host)) throw new ArgumentException(L.T("仅解析 HTTPS Google Maps 链接；也可直接输入地址或经纬度。"));
                for (int count = 0; count < 5; count++)
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        var redirect = response.Headers.Location;
                        if (redirect == null) break;
                        url = redirect.IsAbsoluteUri ? redirect : new Uri(url, redirect);
                        if (url.Scheme != "https" || !CoordinateParser.IsGoogleHost(url.Host)) throw new InvalidDataException(L.T("地图短链接跳转到了未支持的域名，已停止解析。"));
                        if (CoordinateParser.TryParse(url.AbsoluteUri, out parsed)) return new List<SearchResult> { new SearchResult { Title = L.T("Google Maps 链接坐标"), Detail = parsed.Coordinates, Location = parsed } };
                    }
                }
                throw new ArgumentException(L.T("链接中没有可确认的坐标。请在 Google Maps 点选具体地点后复制包含坐标的完整链接，或复制经纬度。"));
            }
            await gate.WaitAsync();
            try
            {
                if (provider == "Google")
                {
                    if (string.IsNullOrWhiteSpace(googleKey)) throw new InvalidOperationException(L.T("先到设置中保存 Google Geocoding API Key，或切换到免 Key 的 OpenStreetMap。"));
                    string endpoint = "https://maps.googleapis.com/maps/api/geocode/json?language=zh-CN&address=" + Uri.EscapeDataString(text) + "&key=" + Uri.EscapeDataString(googleKey);
                    var data = new JavaScriptSerializer().DeserializeObject(await GetAsync(endpoint)) as Dictionary<string, object>;
                    string status = Convert.ToString(data["status"]);
                    if (status == "ZERO_RESULTS") return new List<SearchResult>();
                    if (status != "OK") throw new InvalidOperationException(L.T("Google 地址查询失败（") + status + L.T("）。请检查 Key、Geocoding API、账单与配额；未自动切换到其他服务。"));
                    var results = new List<SearchResult>();
                    foreach (Dictionary<string, object> item in (object[])data["results"])
                    {
                        var geometry = (Dictionary<string, object>)item["geometry"];
                        var point = (Dictionary<string, object>)geometry["location"];
                        var p = new LocationProfile { Name = Convert.ToString(item["formatted_address"]), Latitude = Convert.ToDouble(point["lat"]), Longitude = Convert.ToDouble(point["lng"]), Source = "Google Geocoding" };
                        if (p.Name.Length > 120) p.Name = p.Name.Substring(0, 120);
                        p.Validate();
                        results.Add(new SearchResult { Title = p.Name, Detail = p.Coordinates + " · Google", Location = p });
                    }
                    return results.Take(6).ToList();
                }
                List<SearchResult> cached;
                if (osmCache.TryGetValue(text, out cached)) return cached;
                double delay = 1100 - (DateTime.UtcNow - lastOsmRequest).TotalMilliseconds;
                if (delay > 0) await Task.Delay((int)delay);
                lastOsmRequest = DateTime.UtcNow;
                string json = await GetAsync("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=6&q=" + Uri.EscapeDataString(text));
                var array = new JavaScriptSerializer().DeserializeObject(json) as object[];
                var locations = new List<SearchResult>();
                foreach (Dictionary<string, object> item in array ?? new object[0])
                {
                    string fullName = Convert.ToString(item["display_name"]);
                    var p = new LocationProfile { Name = fullName.Length > 120 ? fullName.Substring(0, 120) : fullName, Latitude = double.Parse(Convert.ToString(item["lat"]), CultureInfo.InvariantCulture), Longitude = double.Parse(Convert.ToString(item["lon"]), CultureInfo.InvariantCulture), Source = "OpenStreetMap / Nominatim" };
                    p.Validate();
                    locations.Add(new SearchResult { Title = fullName, Detail = p.Coordinates + " · OpenStreetMap", Location = p });
                }
                if (osmCache.Count >= 30) osmCache.Clear();
                osmCache[text] = locations;
                return locations;
            }
            catch (TaskCanceledException) { throw new TimeoutException(L.T("地址搜索超时，请检查网络或稍后重试。")); }
            finally { gate.Release(); }
        }
        async Task<string> GetAsync(string url)
        {
            using (var response = await client.GetAsync(url))
            {
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(L.T("地址服务返回 HTTP ") + (int)response.StatusCode + L.T("，请稍后重试。"));
                return await response.Content.ReadAsStringAsync();
            }
        }
        public void Dispose() { client.Dispose(); gate.Dispose(); }
    }
}
