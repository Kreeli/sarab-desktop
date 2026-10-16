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
        internal const int ElevatedOperationTimeoutMilliseconds = 5 * 60 * 1000;
        public const string RegistryPath = @"SYSTEM\CurrentControlSet\Services\SarabGnss\Parameters";
        public const string SarabHash = "8D8614A9B44658D2A1C4440E829078B6FE61C9E8AB735D53AEDFEAE33E2AA0E4";
        public const string ProbeHash = BuildTimePins.ProbeHash;
        public const string HelperHash = BuildTimePins.HelperHash;
        public const string RuntimeHash = BuildTimePins.RuntimeHash;
        public static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
        public static string SarabPath => Path.Combine(BaseDirectory, "backend", "sarab.exe");
        public static string ProbePath => Path.Combine(BaseDirectory, "backend", "location-probe.exe");
        public static string HelperPath => Path.Combine(BaseDirectory, "SarabDesktop.Helper.exe");
        public static string RuntimePath => Path.Combine(BaseDirectory, "backend", "vcruntime140.dll");
        public static void VerifyCliRuntime() => VerifyFile(RuntimePath, RuntimeHash);
        public static bool IsProtectedInstallDirectory(string baseDirectory, string programFilesDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(programFilesDirectory)) return false;
                string expected = Path.GetFullPath(Path.Combine(programFilesDirectory, "SarabDesktop")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string actual = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        public static void RequireProtectedInstallDirectory()
        {
            if (!IsProtectedInstallDirectory(BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)))
                throw new InvalidOperationException(L.T("管理员操作只能从 Program Files 下安装版本执行。请运行安装程序；便携版仅支持普通操作。"));
            string[] directories = { BaseDirectory, Path.Combine(BaseDirectory, "backend"), Path.Combine(BaseDirectory, "driver") };
            foreach (string directory in directories)
                if (!Directory.Exists(directory) || IsReparsePoint(directory)) throw new InvalidOperationException(L.T("管理员操作只能从 Program Files 下安装版本执行。请运行安装程序；便携版仅支持普通操作。"));
            foreach (string file in new[] { HelperPath, SarabPath, ProbePath, RuntimePath, Path.Combine(Installation.DriverDirectory, "SarabGnss.dll"), Path.Combine(Installation.DriverDirectory, "SarabGnss.cat"), Path.Combine(Installation.DriverDirectory, "SarabGnss.inf"), Path.Combine(Installation.DriverDirectory, "SarabGnss.cer") })
                if (File.Exists(file) && IsReparsePoint(file)) throw new InvalidOperationException(L.T("管理员操作只能从 Program Files 下安装版本执行。请运行安装程序；便携版仅支持普通操作。"));
        }
        static bool IsReparsePoint(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return true; }
        }
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
                    {
                        try
                        {
                            var configured = new LocationProfile { Name = L.T("驱动当前配置"), Latitude = lat, Longitude = lon, Altitude = ReadDouble(key, "Altitude", 0), Accuracy = ReadDouble(key, "Accuracy", 5), Drift = Convert.ToInt32(key.GetValue("DriftEnabled", 0)) != 0, DriftRadius = ReadDouble(key, "DriftRadius", 1.2) };
                            configured.Validate();
                            state.Configured = configured;
                        }
                        catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidCastException || ex is OverflowException) { state.Configured = null; }
                    }
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
            if (!LocationVerification.IsValidProbe(value)) throw new InvalidDataException(L.T("定位读取器返回了无效结果。"));
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
            RequireProtectedInstallDirectory();
            if (!File.Exists(HelperPath)) throw new FileNotFoundException(L.T("缺少 SarabDesktop.Helper.exe，请保持发行目录完整。"));
            VerifyFile(HelperPath, HelperHash);
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(HelperPath, "--language " + L.Language + " " + arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = BaseDirectory }))
                {
                    if (process == null) throw new InvalidOperationException(L.T("管理员操作未启动。"));
                    bool exited = await Task.Run(() => process.WaitForExit(ElevatedOperationTimeoutMilliseconds));
                    if (!exited)
                    {
                        try { process.Kill(); process.WaitForExit(); } catch { }
                        throw new TimeoutException(L.T("管理员操作超时；请刷新页面并检查 Windows 实际状态。"));
                    }
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
            get
            {
                try { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return string.Equals(Convert.ToString(key?.GetValue(Name)), Command, StringComparison.OrdinalIgnoreCase); }
                catch (UnauthorizedAccessException) { return false; }
                catch (System.Security.SecurityException) { return false; }
            }
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
        const int MaxGeocodingResponseBytes = 1024 * 1024;
        readonly HttpClient client;
        readonly TimeSpan requestTimeout;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        long lastOsmRequestTimestamp;
        bool hasOsmRequestTimestamp;
        readonly Dictionary<string, List<SearchResult>> osmCache = new Dictionary<string, List<SearchResult>>();
        volatile string requestLanguage;
        volatile bool disposed;
        public GeocodingService() : this(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { }
        internal GeocodingService(HttpMessageHandler handler) : this(handler, TimeSpan.FromSeconds(18)) { }
        internal GeocodingService(HttpMessageHandler handler, TimeSpan requestTimeout)
        {
            if (requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            this.requestTimeout = requestTimeout;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            client = new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))) { Timeout = TimeSpan.FromSeconds(18) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SarabDesktop/1.0 (Windows; manual address lookup)");
            SetLanguage(L.Language);
        }
        public void SetLanguage(string language)
        {
            requestLanguage = language == "zh-TW" || language == "zh-CN" || language == "ja-JP" ? language : "en-US";
        }
        internal static string AcceptLanguageFor(string language) => language == "zh-TW" ? "zh-TW,en;q=0.8" : language == "zh-CN" ? "zh-CN,en;q=0.8" : language == "ja-JP" ? "ja,en;q=0.8" : "en-US,en;q=0.8";
        internal static string GoogleLanguageFor(string language) => language == "zh-TW" ? "zh-TW" : language == "zh-CN" ? "zh-CN" : language == "ja-JP" ? "ja" : "en";
        public async Task<List<SearchResult>> SearchAsync(string text, string provider, string googleKey)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GeocodingService));
            text = (text ?? "").Trim();
            if (text.Length < 2 || text.Length > 1000) throw new ArgumentException(L.T("请输入 2～1000 个字符的地址、坐标或 Google Maps 链接。"));
            string language = requestLanguage;
            LocationProfile parsed;
            if (CoordinateParser.TryParse(text, out parsed)) return new List<SearchResult> { new SearchResult { Title = parsed.Source, Detail = parsed.Coordinates, Location = parsed } };
            Uri url;
            if (Uri.TryCreate(text, UriKind.Absolute, out url) && (url.Scheme == "http" || url.Scheme == "https"))
            {
                if (url.Scheme != "https" || !CoordinateParser.IsGoogleHost(url.Host)) throw new ArgumentException(L.T("仅解析 HTTPS Google Maps 链接；也可直接输入地址或经纬度。"));
                for (int count = 0; count < 5; count++)
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.TryAddWithoutValidation("Accept-Language", AcceptLanguageFor(language));
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                        {
                            var redirect = response.Headers.Location;
                            if (redirect == null) break;
                            url = redirect.IsAbsoluteUri ? redirect : new Uri(url, redirect);
                            if (url.Scheme != "https" || !CoordinateParser.IsGoogleHost(url.Host)) throw new InvalidDataException(L.T("地图短链接跳转到了未支持的域名，已停止解析。"));
                            if (CoordinateParser.TryParse(url.AbsoluteUri, out parsed)) return new List<SearchResult> { new SearchResult { Title = L.T("Google Maps 链接坐标"), Detail = parsed.Coordinates, Location = parsed } };
                        }
                    }
                }
                throw new ArgumentException(L.T("链接中没有可确认的坐标。请在 Google Maps 点选具体地点后复制包含坐标的完整链接，或复制经纬度。"));
            }
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (provider == "Google")
                {
                    if (string.IsNullOrWhiteSpace(googleKey)) throw new InvalidOperationException(L.T("先到设置中保存 Google Geocoding API Key，或切换到免 Key 的 OpenStreetMap。"));
                    string endpoint = "https://maps.googleapis.com/maps/api/geocode/json?language=" + GoogleLanguageFor(language) + "&address=" + Uri.EscapeDataString(text) + "&key=" + Uri.EscapeDataString(googleKey);
                    var data = DeserializeJson(await GetAsync(endpoint, language).ConfigureAwait(false)) as Dictionary<string, object>;
                    object statusValue;
                    if (data == null || !data.TryGetValue("status", out statusValue) || statusValue == null) throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"));
                    string status = Convert.ToString(statusValue, CultureInfo.InvariantCulture);
                    if (status == "ZERO_RESULTS") return new List<SearchResult>();
                    if (status != "OK") throw new InvalidOperationException(L.T("Google 地址查询失败（") + status + L.T("）。请检查 Key、Geocoding API、账单与配额；未自动切换到其他服务。"));
                    var results = new List<SearchResult>();
                    object rawResults;
                    if (!data.TryGetValue("results", out rawResults) || !(rawResults is object[])) throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"));
                    foreach (object raw in ((object[])rawResults).Take(6))
                    {
                        var item = raw as Dictionary<string, object>;
                        var geometry = GoogleObject(item, "geometry");
                        var point = GoogleObject(geometry, "location");
                        var p = new LocationProfile { Name = GoogleString(item, "formatted_address"), Latitude = GoogleNumber(point, "lat"), Longitude = GoogleNumber(point, "lng"), Source = "Google Geocoding" };
                        if (p.Name.Length > 120) p.Name = p.Name.Substring(0, 120);
                        p.Validate();
                        results.Add(new SearchResult { Title = p.Name, Detail = p.Coordinates + " · Google", Location = p });
                    }
                    return results.Take(6).ToList();
                }
                string cacheKey = language + "\n" + text;
                List<SearchResult> cached;
                if (osmCache.TryGetValue(cacheKey, out cached)) return cached;
                if (hasOsmRequestTimestamp)
                {
                    double elapsedMilliseconds = (Stopwatch.GetTimestamp() - lastOsmRequestTimestamp) * 1000.0 / Stopwatch.Frequency;
                    int delay = (int)Math.Ceiling(Math.Max(0, 1100 - elapsedMilliseconds));
                    if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);
                }
                lastOsmRequestTimestamp = Stopwatch.GetTimestamp();
                hasOsmRequestTimestamp = true;
                string json = await GetAsync("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=6&q=" + Uri.EscapeDataString(text), language).ConfigureAwait(false);
                var array = DeserializeJson(json) as object[];
                if (array == null) throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"));
                var locations = new List<SearchResult>();
                foreach (object raw in (array ?? new object[0]).Take(6))
                {
                    var item = raw as Dictionary<string, object>;
                    string fullName = GoogleString(item, "display_name");
                    var p = new LocationProfile { Name = fullName.Length > 120 ? fullName.Substring(0, 120) : fullName, Latitude = GoogleNumber(item, "lat"), Longitude = GoogleNumber(item, "lon"), Source = "OpenStreetMap / Nominatim" };
                    p.Validate();
                    locations.Add(new SearchResult { Title = p.Name, Detail = p.Coordinates + " · OpenStreetMap", Location = p });
                }
                if (osmCache.Count >= 30) osmCache.Clear();
                osmCache[cacheKey] = locations;
                return locations;
            }
            catch (TaskCanceledException) { throw new TimeoutException(L.T("地址搜索超时，请检查网络或稍后重试。")); }
            finally { gate.Release(); }
        }
        async Task<string> GetAsync(string url, string language)
        {
            using (var timeout = new CancellationTokenSource(requestTimeout))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("Accept-Language", AcceptLanguageFor(language));
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(L.T("地址服务返回 HTTP ") + (int)response.StatusCode + L.T("，请稍后重试。"));
                    using (var stream = await AwaitBodyAsync(response.Content.ReadAsStreamAsync(), timeout.Token).ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        var chunk = new byte[8192];
                        int count;
                        while ((count = await AwaitBodyAsync(stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token), timeout.Token).ConfigureAwait(false)) > 0)
                        {
                            if (buffer.Length + count > MaxGeocodingResponseBytes) throw new InvalidDataException(L.T("地址服务返回的数据过大，已停止读取。"));
                            buffer.Write(chunk, 0, count);
                        }
                        byte[] bytes = buffer.ToArray();
                        int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                        try { return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset); }
                        catch (DecoderFallbackException) { throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。")); }
                    }
                }
            }
        }
        static async Task<T> AwaitBodyAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(operation, cancelled.Task).ConfigureAwait(false) != operation)
                {
                    // Older Framework streams can ignore cancellation. Return at the deadline;
                    // the enclosing using statements close the response and its stream.
                    _ = operation.ContinueWith(task => { var ignored = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    throw new TaskCanceledException();
                }
                return await operation.ConfigureAwait(false);
            }
        }
        static object DeserializeJson(string json)
        {
            try { return new JavaScriptSerializer { MaxJsonLength = MaxGeocodingResponseBytes }.DeserializeObject(json); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            { throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"), ex); }
        }
        static Dictionary<string, object> GoogleObject(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value) || !(value is Dictionary<string, object>)) throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"));
            return (Dictionary<string, object>)value;
        }
        static string GoogleString(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value) || value == null) throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"));
            string result = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"));
            return result;
        }
        static double GoogleNumber(Dictionary<string, object> parent, string key)
        {
            object value;
            double result;
            if (parent == null || !parent.TryGetValue(key, out value) || value == null || !double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result) || double.IsNaN(result) || double.IsInfinity(result)) throw new InvalidDataException(L.T("地址服务返回的数据格式无效，请稍后重试。"));
            return result;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.Dispose();
            // Searches may still be unwinding through finally; keep the semaphore alive so Release cannot race disposal.
        }
    }
}
