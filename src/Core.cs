using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Script.Serialization;

namespace SarabDesktop
{
    public sealed class LocationProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = L.T("未命名位置");
        public double Latitude { get; set; } = 51.5074;
        public double Longitude { get; set; } = -0.1278;
        public double Altitude { get; set; }
        public double Accuracy { get; set; } = 5;
        public bool Drift { get; set; }
        public double DriftRadius { get; set; } = 1.2;
        public string Source { get; set; } = L.T("手动输入");
        public string Coordinates => Latitude.ToString("F7", CultureInfo.InvariantCulture) + ", " + Longitude.ToString("F7", CultureInfo.InvariantCulture);
        public LocationProfile Copy() => Json.From<LocationProfile>(Json.To(this));
        public static LocationProfile Example() => new LocationProfile { Name = L.T("示例位置（尚未应用）"), Source = L.T("示例坐标") };
        public void Validate()
        {
            if (!CoordinateParser.Valid(Latitude, Longitude)) throw new ArgumentException(L.T("纬度应为 -90～90，经度应为 -180～180。"));
            if (!Finite(Altitude) || Math.Abs(Altitude) > 100000) throw new ArgumentException(L.T("海拔应在 -100000～100000 米之间。"));
            if (!Finite(Accuracy) || Accuracy < 0.1 || Accuracy > 100000) throw new ArgumentException(L.T("精度半径应在 0.1～100000 米之间（驱动保留一位小数）。"));
            if (!Finite(DriftRadius) || DriftRadius < 0 || DriftRadius > 50) throw new ArgumentException(L.T("漂移半径应在 0～50 米之间。"));
            if (string.IsNullOrWhiteSpace(Name)) Name = L.T("自定义位置");
            if (Name.Length > 120) throw new ArgumentException(L.T("位置名称不能超过 120 个字符。"));
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class AppSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public string Language { get; set; } = L.SystemLanguage;
        public LocationProfile Target { get; set; } = LocationProfile.Example();
        public List<LocationProfile> Favorites { get; set; } = new List<LocationProfile>();
        public bool CloseToTray { get; set; } = true;
        public bool StartMinimized { get; set; } = true;
        public bool VerifyOnStartup { get; set; } = true;
        public bool VerifyAfterApply { get; set; } = true;
        public string Geocoder { get; set; } = "OpenStreetMap";
        public string GoogleKeyProtected { get; set; }
        public string LastAppliedUtc { get; set; }
    }

    public static class Json
    {
        public static string To(object value) => new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Serialize(value);
        public static T From<T>(string value) => new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<T>(value);
    }

    public sealed class SettingsStore
    {
        public string DirectoryPath { get; }
        public string FilePath => Path.Combine(DirectoryPath, "settings.json");
        public event Action<string> Logged;
        public SettingsStore(string directory = null)
        {
            DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SarabDesktop");
            Directory.CreateDirectory(DirectoryPath);
        }
        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var s = Json.From<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (s == null || s.Target == null) throw new InvalidDataException(L.T("配置缺少目标坐标。"));
                s.Target.Validate();
                s.Favorites = (s.Favorites ?? new List<LocationProfile>()).Where(p => p != null && CoordinateParser.Valid(p.Latitude, p.Longitude)).Take(200).ToList();
                if (s.Geocoder != "Google" && s.Geocoder != "OpenStreetMap") s.Geocoder = "OpenStreetMap";
                if (s.Language != "zh-CN" && s.Language != "en-US") s.Language = L.SystemLanguage;
                return s;
            }
            catch (Exception ex)
            {
                var backup = FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                try { File.Copy(FilePath, backup); } catch { }
                Log(L.T("配置读取失败，原文件已尽量备份：") + ex.Message);
                return new AppSettings();
            }
        }
        public void Save(AppSettings settings)
        {
            settings.Target.Validate();
            string temporary = FilePath + ".new";
            File.WriteAllText(temporary, Json.To(settings), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".backup");
            else File.Move(temporary, FilePath);
        }
        public void Log(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            try
            {
                string path = Path.Combine(DirectoryPath, "activity.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Move(path, path + "." + DateTime.Now.ToString("yyyyMMddHHmmss"));
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
            Logged?.Invoke(line);
        }
        public string ReadLog()
        {
            string p = Path.Combine(DirectoryPath, "activity.log");
            return File.Exists(p) ? string.Join(Environment.NewLine, File.ReadAllLines(p, Encoding.UTF8).Reverse().Take(180)) : L.T("还没有操作记录。");
        }
        public static string Protect(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
        public static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }

    public static class CoordinateParser
    {
        const string Number = @"[-+]?\d{1,3}(?:\.\d+)?";
        public static bool Valid(double lat, double lon) => !double.IsNaN(lat) && !double.IsNaN(lon) && !double.IsInfinity(lat) && !double.IsInfinity(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
        static double Parse(string n) => double.Parse(n, CultureInfo.InvariantCulture);
        public static bool IsGoogleHost(string host)
        {
            return new[] { "google.com", "www.google.com", "maps.google.com", "google.co.jp", "www.google.co.jp", "maps.google.co.jp", "maps.app.goo.gl", "goo.gl" }.Contains(host.ToLowerInvariant());
        }
        public static bool TryParse(string input, out LocationProfile location)
        {
            location = null;
            if (string.IsNullOrWhiteSpace(input) || input.Length > 8192) return false;
            input = input.Trim();
            Uri uri;
            if (Uri.TryCreate(input, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            {
                if (uri.Scheme != "https" || !IsGoogleHost(uri.Host)) return false;
                string decoded = Uri.UnescapeDataString(input);
                Match place = Regex.Match(decoded, @"!3d(" + Number + @")!4d(" + Number + @")", RegexOptions.IgnoreCase);
                if (place.Success) return Make(Parse(place.Groups[1].Value), Parse(place.Groups[2].Value), L.T("Google Maps 地点坐标"), out location);
                var query = HttpUtility.ParseQueryString(uri.Query);
                foreach (string key in new[] { "query", "q", "ll", "center", "destination", "daddr" })
                {
                    LocationProfile pair;
                    if (TryPair(query[key], out pair)) { pair.Source = L.T("Google Maps 链接"); location = pair; return true; }
                }
                Match viewport = Regex.Match(decoded, @"@(" + Number + @"),(" + Number + @"),", RegexOptions.IgnoreCase);
                if (viewport.Success) return Make(Parse(viewport.Groups[1].Value), Parse(viewport.Groups[2].Value), L.T("Google Maps 视图中心（请核对）"), out location);
                return false;
            }
            if (TryPair(input, out location)) return true;
            string normalized = input.Replace('′', '\'').Replace('’', '\'').Replace('″', '"').Replace('“', '"').Replace('”', '"');
            string dms = @"(?<dir>[NSEW])\s*(?<d>\d{1,3})\s*[°度]\s*(?<m>\d{1,2})\s*['分]\s*(?<s>\d{1,2}(?:\.\d+)?)\s*(?:[""秒])?";
            var matches = Regex.Matches(normalized, dms, RegexOptions.IgnoreCase);
            if (matches.Count != 2)
            {
                string suffix = @"(?<d>\d{1,3})\s*[°度]\s*(?<m>\d{1,2})\s*['分]\s*(?<s>\d{1,2}(?:\.\d+)?)\s*[""秒]?\s*(?<dir>[NSEW])";
                matches = Regex.Matches(normalized, suffix, RegexOptions.IgnoreCase);
            }
            if (matches.Count != 2) return false;
            double? lat = null, lon = null;
            foreach (Match m in matches)
            {
                double minutes = Parse(m.Groups["m"].Value), seconds = Parse(m.Groups["s"].Value);
                if (minutes >= 60 || seconds >= 60) return false;
                double value = Parse(m.Groups["d"].Value) + minutes / 60 + seconds / 3600;
                string dir = m.Groups["dir"].Value.ToUpperInvariant();
                if (dir == "S" || dir == "W") value = -value;
                if (dir == "N" || dir == "S") { if (lat.HasValue) return false; lat = value; }
                else { if (lon.HasValue) return false; lon = value; }
            }
            return lat.HasValue && lon.HasValue && Make(lat.Value, lon.Value, L.T("度分秒坐标"), out location);
        }
        static bool TryPair(string text, out LocationProfile location)
        {
            location = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var m = Regex.Match(text.Trim(), @"^(" + Number + @")\s*[,，]\s*(" + Number + @")$");
            return m.Success && Make(Parse(m.Groups[1].Value), Parse(m.Groups[2].Value), L.T("十进制度坐标"), out location);
        }
        static bool Make(double lat, double lon, string source, out LocationProfile location)
        {
            location = null;
            if (!Valid(lat, lon)) return false;
            location = new LocationProfile { Latitude = lat, Longitude = lon, Name = L.T("地图选点"), Source = source };
            return true;
        }
        public static double Distance(double lat, double lon, double targetLat, double targetLon)
        {
            double rad = Math.PI / 180;
            double a = Math.Pow(Math.Sin((lat - targetLat) * rad / 2), 2) + Math.Cos(lat * rad) * Math.Cos(targetLat * rad) * Math.Pow(Math.Sin((lon - targetLon) * rad / 2), 2);
            return 6371000 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
        }
    }

    public sealed class ProbeResult
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
        public double accuracyMeters { get; set; }
        public int positionSource { get; set; }
        public long timestampTicks { get; set; }
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        public string SourceName => new[] { L.T("蜂窝网络"), L.T("卫星 / 虚拟 GNSS"), "Wi-Fi", L.T("IP 地址"), L.T("未知"), L.T("默认位置"), L.T("模糊位置") }.ElementAtOrDefault(positionSource) ?? L.T("未知");
    }
    public sealed class DriverState
    {
        public bool Installed { get; set; }
        public bool Healthy { get; set; }
        public string DeviceName { get; set; } = L.T("未发现 Sarab 设备");
        public int ProblemCode { get; set; } = -1;
        public bool Enabled { get; set; }
        public bool LocationAllowed { get; set; }
        public bool TestSigning { get; set; }
        public LocationProfile Configured { get; set; }
    }
    public sealed class SearchResult
    {
        public string Title { get; set; }
        public string Detail { get; set; }
        public LocationProfile Location { get; set; }
        public override string ToString() => Title;
    }
}
