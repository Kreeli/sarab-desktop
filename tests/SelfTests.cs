using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SarabDesktop
{
    public static class SelfTests
    {
        static readonly List<object> results = new List<object>();
        static int failures;
        static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
        static void Test(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.Message }); }
        }
        static void Pair(string text, double lat, double lon)
        {
            LocationProfile p;
            Assert(CoordinateParser.TryParse(text, out p), "无法解析");
            Assert(Math.Abs(p.Latitude - lat) < 0.00000001 && Math.Abs(p.Longitude - lon) < 0.00000001, "坐标不匹配");
        }
        static void Reject(string text) { LocationProfile p; Assert(!CoordinateParser.TryParse(text, out p), "应拒绝无效输入"); }
        static void Invalid(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("应抛出参数错误"); }
        static void Write(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllText(path, Json.To(value), new System.Text.UTF8Encoding(false)); }
        public static int Run(string report)
        {
            Test("前缀方向度分秒", () => Pair("W0°7'40.08\" N51°30'26.64\"", 51.5074, -0.1278));
            Test("后缀方向与 Unicode 度分秒", () => Pair("51°30′26.64″N 0°7′40.08″W", 51.5074, -0.1278));
            Test("南纬西经", () => Pair("S34°30'0\" W135°30'0\"", -34.5, -135.5));
            Test("小数坐标", () => Pair("51.5074，-0.1278", 51.5074, -0.1278));
            Test("Google Maps API 链接", () => Pair("https://www.google.com/maps/search/?api=1&query=51.5074%2C-0.1278", 51.5074, -0.1278));
            Test("优先地点坐标而不是视图中心", () => Pair("https://www.google.com/maps/place/example/@35.0,139.0,12z/data=!3d51.5074!4d-0.1278", 51.5074, -0.1278));
            Test("Google Maps 视图中心有明确标记", () => { LocationProfile p; Assert(CoordinateParser.TryParse("https://www.google.co.jp/maps/@34.5,135.5,15z", out p) && p.Source == L.T("Google Maps 视图中心（请核对）"), "来源标记缺失"); });
            Test("越界经纬度", () => { Reject("91,0"); Reject("0,181"); Reject("NaN,0"); Reject("Infinity,0"); });
            Test("无效分秒与重复方向", () => { Reject("N34°60'0\" E135°0'0\""); Reject("N34°0'60\" E135°0'0\""); Reject("N34°0'0\" N35°0'0\""); });
            Test("拒绝非 Google 和不安全链接", () => { Reject("https://google.com.example.org/maps/@34,135,15z"); Reject("http://www.google.com/maps/@34,135,15z"); });
            Test("数值边界验证", () => { Invalid(() => new LocationProfile { Latitude = double.NaN }.Validate()); Invalid(() => new LocationProfile { Accuracy = 0 }.Validate()); Invalid(() => new LocationProfile { DriftRadius = 51 }.Validate()); Invalid(() => new LocationProfile { Altitude = double.PositiveInfinity }.Validate()); new LocationProfile { Latitude = -90, Longitude = 180, DriftRadius = 0 }.Validate(); });
            Test("跨文化数值传参", () => { var old = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); var expected = LocationProfile.Example(); string[] arguments = Backend.ApplyArguments(expected).Split(' '); Assert(arguments.Length == 7 && double.Parse(arguments[1], CultureInfo.InvariantCulture) == expected.Latitude && double.Parse(arguments[2], CultureInfo.InvariantCulture) == expected.Longitude && arguments[5] == "0", "小数格式被系统区域更改"); } finally { CultureInfo.CurrentCulture = old; } });
            Test("负坐标与负海拔传给上游 CLI", () => { string command = Backend.SarabSetArguments(new LocationProfile { Latitude = -34.5, Longitude = -135.5, Altitude = -20 }); Assert(command.StartsWith("set --lat=-34.5 --lon=-135.5 --alt=-20 "), "负数被误识别为命令行选项"); });
            Test("两点距离", () => { Assert(CoordinateParser.Distance(34, 135, 34, 135) == 0, "同点不为零"); Assert(Math.Abs(CoordinateParser.Distance(0, 0, 0, 1) - 111194.9) < 1, "球面距离计算错误"); });
            Test("配置原子写入与加密 Key", () =>
            {
                var store = new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "isolated-settings"));
                const string secret = "selftest-only-dummy-key";
                var s = new AppSettings { GoogleKeyProtected = SettingsStore.Protect(secret) };
                store.Save(s); s.Target.Name = "测试第二次写入"; store.Save(s);
                var loaded = store.Load();
                Assert(loaded.Target.Name == s.Target.Name && SettingsStore.Unprotect(loaded.GoogleKeyProtected) == secret, "往返失败");
                Assert(!File.ReadAllText(store.FilePath).Contains(secret), "Key 明文落盘");
                string export = Json.To(new DesktopController.ProfileBundle { Favorites = s.Favorites });
                Assert(!export.Contains("Key") && !export.Contains(secret), "收藏导出含密钥");
            });
            Test("登录启动项实际写入后清理", () => { bool before = StartupRegistration.Enabled; StartupRegistration.TestTemporaryRegistration(); Assert(StartupRegistration.Enabled == before, "正式启动项被修改"); });
            Test("后端文件 SHA256", () => { Backend.VerifyFile(Backend.SarabPath, Backend.SarabHash); Backend.VerifyFile(Backend.ProbePath, Backend.ProbeHash); });
            Test("固定驱动包和证书校验", Installation.VerifyDriverPackage);
            Test("中英动态状态与校验错误", () => { string old = L.Language; try { L.Set("en-US"); Assert(new ProbeResult { positionSource = 1 }.SourceName == "Satellite / virtual GNSS", "动态定位来源未翻译"); Assert(L.T("应用并核验") == "Apply and verify" && L.TranslationCount > 250, "英文资源缺失"); try { new LocationProfile { Latitude = 91 }.Validate(); } catch (ArgumentException ex) { Assert(!ex.Message.Any(c => c >= '\u3400' && c <= '\u9fff'), "英文错误中残留中文"); } L.Set("zh-CN"); Assert(new ProbeResult { positionSource = 1 }.SourceName == "卫星 / 虚拟 GNSS", "中文状态错误"); } finally { L.Set(old); } });
            Test("安装策略拦截不支持架构和不安全启动修改", () =>
            {
                var s = new PreflightState { IsAmd64 = true, WindowsBuild = 22621, SecureBoot = false, SystemDiskReadyForBootChange = true };
                Assert(InstallationPolicy.PreparationBlockReason(s) == null, "合法准备状态被阻止");
                s.SecureBoot = true; Assert(InstallationPolicy.PreparationBlockReason(s) != null, "Secure Boot 开启未被拦截");
                s.SecureBoot = null; Assert(InstallationPolicy.PreparationBlockReason(s) != null, "未知固件状态未被拦截");
                s.SecureBoot = false; s.SystemDiskReadyForBootChange = false; Assert(InstallationPolicy.PreparationBlockReason(s) != null, "已加密磁盘未被拦截");
                s.SystemDiskReadyForBootChange = null; Assert(InstallationPolicy.PreparationBlockReason(s) != null, "未知磁盘状态未被拦截");
                s.TestSigning = true; Assert(InstallationPolicy.PreparationBlockReason(s) == null, "已启用测试模式仍尝试修改启动配置");
                s.IsAmd64 = false; Assert(InstallationPolicy.PreparationBlockReason(s) != null, "ARM64 未被拦截");
                s.IsAmd64 = true; s.WindowsBuild = 10000; Assert(InstallationPolicy.PreparationBlockReason(s) != null, "不支持的系统未被拦截");
            });
            Test("离线查询无需 Google Key", () => { using (var service = new GeocodingService()) { var matches = service.SearchAsync("51.5074,-0.1278", "Google", "").GetAwaiter().GetResult(); Assert(matches.Count == 1 && matches[0].Location.Latitude == 51.5074, "离线解析失败"); } });
            Test("Google 无 Key 不联网查询", () => { using (var service = new GeocodingService()) { try { service.SearchAsync("Tokyo Station", "Google", "").GetAwaiter().GetResult(); } catch (InvalidOperationException ex) { Assert(ex.Message.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0, "错误说明不明确"); return; } throw new Exception("未拒绝缺失 Key"); } });
            Write(report, new { createdUtc = DateTime.UtcNow.ToString("o"), passed = failures == 0, count = results.Count, failures, results });
            return failures == 0 ? 0 : 1;
        }
        public static async Task<int> CheckBackendAsync(string report)
        {
            try
            {
                var state = await Backend.ReadStateAsync(); var probe = await Backend.ProbeAsync();
                double? distance = state.Configured == null ? (double?)null : CoordinateParser.Distance(state.Configured.Latitude, state.Configured.Longitude, probe.latitude, probe.longitude);
                bool passed = state.Healthy && state.Enabled && state.LocationAllowed && probe.positionSource == 1 && distance < 25;
                Write(report, new { passed, createdUtc = DateTime.UtcNow.ToString("o"), state, probe, distanceMeters = distance, autostartEnabled = StartupRegistration.Enabled });
                return passed ? 0 : 1;
            }
            catch (Exception ex) { Write(report, new { passed = false, error = ex.ToString() }); return 1; }
        }
        public static async Task<int> SearchSmokeAsync(string report)
        {
            try
            {
                using (var service = new GeocodingService())
                {
                    var found = await service.SearchAsync("Tokyo Station Japan", "OpenStreetMap", "");
                    Assert(found.Count > 0 && found.All(r => r.Location.Latitude > 35 && r.Location.Latitude < 36 && r.Location.Longitude > 139 && r.Location.Longitude < 140), "公共地标搜索没有返回预期范围");
                    Write(report, new { passed = true, provider = "OpenStreetMap / Nominatim", query = "Tokyo Station Japan", results = found }); return 0;
                }
            }
            catch (Exception ex) { Write(report, new { passed = false, error = ex.Message }); return 1; }
        }
    }
}
