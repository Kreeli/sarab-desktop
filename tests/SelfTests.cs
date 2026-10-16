using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SarabDesktop
{
    public static class SelfTests
    {
        static readonly List<object> results = new List<object>();
        static int failures;
        static int skipped;
        sealed class TestSkippedException : Exception { public TestSkippedException(string message) : base(message) { } }
        sealed class UncancellableBodyStream : Stream
        {
            readonly TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
            public bool WasDisposed { get; private set; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => pending.Task;
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
        }
        sealed class FakeGeocodingHandler : HttpMessageHandler
        {
            public readonly List<Tuple<Uri, string>> Requests = new List<Tuple<Uri, string>>();
            public Func<Uri, string, string> ResponseForRequest = (uri, language) => "{\"status\":\"OK\",\"results\":[{\"formatted_address\":\"Tokyo Station, Japan\",\"geometry\":{\"location\":{\"lat\":35.681236,\"lng\":139.767125}}}]}";
            public Func<Uri, HttpResponseMessage> HttpResponseForRequest;
            public bool BlockFirstRequest;
            int requestCount;
            public readonly TaskCompletionSource<bool> FirstRequestStarted = new TaskCompletionSource<bool>();
            public readonly TaskCompletionSource<bool> ReleaseFirstRequest = new TaskCompletionSource<bool>();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string language = string.Join(",", request.Headers.GetValues("Accept-Language")).Replace(" ", "");
                lock (Requests) Requests.Add(Tuple.Create(request.RequestUri, language));
                if (BlockFirstRequest && Interlocked.Increment(ref requestCount) == 1)
                {
                    FirstRequestStarted.TrySetResult(true);
                    var cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
                    if (await Task.WhenAny(ReleaseFirstRequest.Task, cancelled).ConfigureAwait(false) != ReleaseFirstRequest.Task) cancellationToken.ThrowIfCancellationRequested();
                    await ReleaseFirstRequest.Task.ConfigureAwait(false);
                }
                if (HttpResponseForRequest != null) return HttpResponseForRequest(request.RequestUri);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseForRequest(request.RequestUri, language), System.Text.Encoding.UTF8, "application/json") };
            }
        }
        static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
        static void Test(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); }
            catch (TestSkippedException ex) { skipped++; results.Add(new { name, skipped = true, reason = ex.Message }); }
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
            Test("两点距离和反足点边界", () =>
            {
                Assert(CoordinateParser.Distance(34, 135, 34, 135) == 0, "同点不为零");
                Assert(Math.Abs(CoordinateParser.Distance(0, 0, 0, 1) - 111194.9) < 1, "球面距离计算错误");
                double antipodal = CoordinateParser.Distance(0, 0, 0, 180);
                Assert(!double.IsNaN(antipodal) && !double.IsInfinity(antipodal) && Math.Abs(antipodal - Math.PI * 6371000) < 0.01, "反足点距离超出浮点边界");
                Invalid(() => CoordinateParser.Distance(double.NaN, 0, 0, 0));
            });
            Test("定位核验拒绝异常精度和过大容差", () =>
            {
                var configured = new LocationProfile { Latitude = 35.681236, Longitude = 139.767125, Drift = true, DriftRadius = 50 };
                var near = new ProbeResult { latitude = configured.Latitude, longitude = configured.Longitude, accuracyMeters = 10, positionSource = 1 };
                Assert(LocationVerification.IsValidProbe(near) && LocationVerification.IsWithinConfiguredTolerance(configured, near), "有效的附近 GNSS 采样未通过");
                var exaggerated = new ProbeResult { latitude = -34.5, longitude = -135.5, accuracyMeters = 1e300, positionSource = 1 };
                Assert(!LocationVerification.IsValidProbe(exaggerated) && !LocationVerification.IsWithinConfiguredTolerance(configured, exaggerated), "极大精度值放大了核验容差");
                near.accuracyMeters = double.PositiveInfinity;
                Assert(!LocationVerification.IsValidProbe(near) && !LocationVerification.IsWithinConfiguredTolerance(configured, near), "无限精度值被接受");
                near.accuracyMeters = 101;
                Assert(!LocationVerification.IsWithinConfiguredTolerance(configured, near), "低可信度采样仍被报告为核验通过");
            });
            Test("提权操作仅允许受保护的固定安装路径", () =>
            {
                Assert(Backend.IsProtectedInstallDirectory(@"C:\Program Files\SarabDesktop\", @"C:\Program Files"), "标准安装路径未被识别");
                Assert(Backend.IsProtectedInstallDirectory(@"c:\program files\sarabdesktop", @"C:\Program Files"), "路径大小写不应影响保护路径识别");
                Assert(!Backend.IsProtectedInstallDirectory(@"C:\Users\tester\AppData\Local\Programs\SarabDesktop", @"C:\Program Files"), "用户可写目录被识别为受保护路径");
                Assert(!Backend.IsProtectedInstallDirectory(@"C:\Program Files\OtherApp", @"C:\Program Files"), "其他安装目录不应通过门禁");
            });
            Test("安装状态ACL拒绝用户控制与空DACL", () =>
            {
                var administrators = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
                var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
                var acl = new System.Security.AccessControl.DirectorySecurity();
                acl.SetOwner(administrators);
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(administrators, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(users, System.Security.AccessControl.FileSystemRights.ReadAndExecute, System.Security.AccessControl.AccessControlType.Allow));
                Assert(Installation.IsTrustedStateSecurity(acl), "管理员所有且普通用户只读的状态目录被拒绝");
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(users, System.Security.AccessControl.FileSystemRights.Write, System.Security.AccessControl.AccessControlType.Allow));
                Assert(!Installation.IsTrustedStateSecurity(acl), "普通用户可写ACL被接受");
                acl.SetOwner(users);
                Assert(!Installation.IsTrustedStateSecurity(acl), "普通用户拥有的目录被接受");
                var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(System.Security.AccessControl.ControlFlags.DiscretionaryAclPresent | System.Security.AccessControl.ControlFlags.SelfRelative, administrators, administrators, null, null);
                var bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0);
                var nullDacl = new System.Security.AccessControl.DirectorySecurity(); nullDacl.SetSecurityDescriptorBinaryForm(bytes);
                Assert(!Installation.IsTrustedStateSecurity(nullDacl), "允许任意访问的空DACL被接受");
                foreach (string rights in new[] { "GA", "GW" })
                {
                    var generic = new System.Security.AccessControl.DirectorySecurity();
                    generic.SetSecurityDescriptorSddlForm("O:BAG:BAD:(A;;FA;;;BA)(A;;" + rights + ";;;BU)");
                    Assert(!Installation.IsTrustedStateSecurity(generic), "普通用户Generic写入权限被接受：" + rights);
                }
            });
            Test("UTF8配置写入上限与大日志尾部读取", () =>
            {
                var store = new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "bounded-output-settings"));
                var settings = new AppSettings(); store.Save(settings);
                string original = File.ReadAllText(store.FilePath);
                settings.Target.Source = new string('中', 400000);
                bool rejected = false;
                try { store.Save(settings); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && File.ReadAllText(store.FilePath) == original, "UTF8超限配置写入后会无法重新读取，或破坏旧配置");
                string log = Path.Combine(store.DirectoryPath, "activity.log");
                using (var output = new StreamWriter(log, false, System.Text.Encoding.UTF8))
                {
                    output.Write(new string('x', 1024 * 1024)); output.WriteLine();
                    for (int i = 0; i < 200; i++) output.WriteLine("bounded-log-line-" + i);
                }
                string tail = store.ReadLog();
                Assert(tail.StartsWith("bounded-log-line-199") && tail.EndsWith("bounded-log-line-20") && tail.Length < 10000, "日志未限制为最新180行，或读取了整个巨大前缀");
            });
            Test("内嵌的 20 首 8-bit 喜报音乐", () =>
            {
                var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(name => name.StartsWith("SarabDesktop.NoticeMusic.track-", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                Assert(resources.Length == 20, "资源数不是 20 首：" + resources.Length);
                for (int i = 0; i < 20; i++)
                {
                    string expectedName = "SarabDesktop.NoticeMusic.track-" + (i + 1).ToString("D2", CultureInfo.InvariantCulture);
                    Assert(resources[i] == expectedName, "音轨资源名称或顺序错误：" + resources[i]);
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resources[i]))
                    using (var reader = new BinaryReader(stream))
                    {
                        byte[] header = reader.ReadBytes(44);
                        Assert(header.Length == 44 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WAVE", "WAV 文件头无效：" + resources[i]);
                        Assert(BitConverter.ToInt32(header, 4) == stream.Length - 8 && BitConverter.ToInt16(header, 20) == 1 && BitConverter.ToInt16(header, 22) == 1 && BitConverter.ToInt32(header, 24) == 11025 && BitConverter.ToInt32(header, 28) == 11025 && BitConverter.ToInt16(header, 32) == 1 && BitConverter.ToInt16(header, 34) == 8, "音轨不是有效的单声道 11.025 kHz / 8-bit PCM：" + resources[i]);
                        int dataBytes = BitConverter.ToInt32(header, 40);
                        Assert(System.Text.Encoding.ASCII.GetString(header, 36, 4) == "data" && dataBytes > 0 && stream.Length == 44L + dataBytes, "音轨 PCM 数据块缺失或长度不匹配：" + resources[i]);
                    }
                }
            });
            Test("内存 PCM 播放器校验与拒绝损坏资源", () =>
            {
                var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(name => name.StartsWith("SarabDesktop.NoticeMusic.track-", StringComparison.Ordinal)).ToArray();
                foreach (string name in resources)
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                        Assert(EmbeddedPcmPlayer.ReadPcm(stream).Length == stream.Length - 44, "原生播放器无法读取音轨：" + name);
                byte[] valid;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resources[0]))
                using (var copy = new MemoryStream()) { stream.CopyTo(copy); valid = copy.ToArray(); }
                foreach (int offset in new[] { 0, 4, 8, 20, 22, 24, 28, 32, 34, 40 })
                {
                    byte[] damaged = (byte[])valid.Clone(); damaged[offset] ^= 0x7f;
                    bool rejected = false;
                    try { using (var stream = new MemoryStream(damaged)) EmbeddedPcmPlayer.ReadPcm(stream); }
                    catch (InvalidDataException) { rejected = true; }
                    Assert(rejected, "损坏的 PCM 字段未被拒绝：" + offset);
                }
                bool truncated = false;
                try { using (var stream = new MemoryStream(valid, 0, valid.Length - 1)) EmbeddedPcmPlayer.ReadPcm(stream); }
                catch (InvalidDataException) { truncated = true; }
                Assert(truncated, "截断的 PCM 资源未被拒绝");
            });
            Test("嵌入喜报生成器原始背景图", () =>
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SarabDesktop.NoticeCertificateBackground.jpg"))
                {
                    Assert(stream != null, "喜报背景图未嵌入应用");
                    using (var reader = new BinaryReader(stream))
                    {
                        byte[] signature = reader.ReadBytes(3);
                        Assert(signature.SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF }) && stream.Length > 10000, "背景资源不是有效 JPEG 或内容为空");
                        stream.Position = stream.Length - 2;
                        Assert(reader.ReadByte() == 0xFF && reader.ReadByte() == 0xD9, "JPEG 文件尾标记无效");
                    }
                }
            });
            Test("喜报音乐随机切换与最高音量", () =>
            {
                Assert(DesktopController.CertificateMusicVolume(false) == 1.0 && DesktopController.CertificateMusicVolume(true) == 0.0, "正式音量未设为最大或预览未静音");
                Assert(DesktopController.ChooseNextNoticeMusicTrackIndex(1, 0, new Random(1)) == 0, "单曲时曲目选择错误");
                var random = new Random(20261016); int previous = -1;
                for (int i = 0; i < 1000; i++)
                {
                    int selected = DesktopController.ChooseNextNoticeMusicTrackIndex(20, previous, random);
                    Assert(selected >= 0 && selected < 20 && selected != previous, "随机曲目越界或连续重复");
                    previous = selected;
                }
            });
            Test("配置原子写入与加密 Key", () =>
            {
                var store = new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "isolated-settings"));
                const string secret = "selftest-only-dummy-key";
                var s = new AppSettings { GoogleKeyProtected = SettingsStore.Protect(secret), SidebarWidth = 286.5 };
                store.Save(s); s.Target.Name = "测试第二次写入"; store.Save(s);
                var loaded = store.Load();
                Assert(loaded.Target.Name == s.Target.Name && SettingsStore.Unprotect(loaded.GoogleKeyProtected) == secret && Math.Abs(loaded.SidebarWidth - 286.5) < 0.01, "配置往返失败");
                Assert(!File.ReadAllText(store.FilePath).Contains(secret), "Key 明文落盘");
                string export = Json.To(new DesktopController.ProfileBundle { Favorites = s.Favorites });
                Assert(!export.Contains("Key") && !export.Contains(secret), "收藏导出含密钥");
            });
            Test("旧程序只读打开未来配置且不覆盖未知字段", () =>
            {
                var store = new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "future-schema-settings"));
                const string future = "{\"SchemaVersion\":999,\"FutureField\":{\"Preserve\":true},\"Target\":{\"Latitude\":1,\"Longitude\":2}}";
                File.WriteAllText(store.FilePath, future, new System.Text.UTF8Encoding(false));
                var loaded = store.Load();
                Assert(store.IsReadOnlyDueToNewerSchema, "新版本配置未进入保护模式");
                try { store.Save(loaded); throw new Exception("未来版本配置意外接受保存"); }
                catch (InvalidOperationException) { }
                Assert(File.ReadAllText(store.FilePath) == future && File.ReadAllText(store.FilePath).Contains("FutureField"), "旧程序覆盖或丢弃了未知配置字段");
            });
            Test("侧栏宽度配置范围保护", () =>
            {
                var store = new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "sidebar-width-settings"));
                File.WriteAllText(store.FilePath, Json.To(new AppSettings { SidebarWidth = 12 }), new System.Text.UTF8Encoding(false));
                Assert(store.Load().SidebarWidth == AppSettings.MinSidebarWidth, "低于最小值的侧栏宽度没有被限制");
                File.WriteAllText(store.FilePath, Json.To(new AppSettings { SidebarWidth = 900 }), new System.Text.UTF8Encoding(false));
                Assert(store.Load().SidebarWidth == AppSettings.MaxSidebarWidth, "超过最大值的侧栏宽度没有被限制");
            });
            Test("配置读取会拒绝完整性无效的收藏", () =>
            {
                var store = new SettingsStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "invalid-favorite-settings"));
                var invalid = new LocationProfile { Latitude = 95, Name = "bad" };
                File.WriteAllText(store.FilePath, Json.To(new AppSettings { Favorites = new List<LocationProfile> { LocationProfile.Example(), invalid } }), new System.Text.UTF8Encoding(false));
                var loaded = store.Load();
                Assert(loaded.Favorites.Count == 1 && loaded.Favorites[0].Name == LocationProfile.Example().Name, "无效收藏未被隔离");
            });
            Test("登录启动项实际写入后清理", () =>
            {
                bool before = StartupRegistration.Enabled;
                try { StartupRegistration.TestTemporaryRegistration(); }
                catch (Exception ex) { if (ex is UnauthorizedAccessException || ex is System.Security.SecurityException) throw new TestSkippedException("运行环境拒绝 HKCU Run 写入；正式启动项未修改。"); throw; }
                Assert(StartupRegistration.Enabled == before, "正式启动项被修改");
            });
            Test("后端与提权辅助程序 SHA256", () => { Backend.VerifyFile(Backend.SarabPath, Backend.SarabHash); Backend.VerifyFile(Backend.ProbePath, Backend.ProbeHash); Backend.VerifyFile(Backend.HelperPath, Backend.HelperHash); });
            Test("固定驱动包和证书校验", Installation.VerifyDriverPackage);
            Test("四语资源完整与系统语言映射", () =>
            {
                foreach (string language in L.SupportedLanguages) Assert(L.TranslationCountFor(language) == L.TranslationCount, "语言资源键数不完整：" + language);
                Assert(L.ResolveCulture("ja-JP") == "ja-JP" && L.ResolveCulture("zh-TW") == "zh-TW" && L.ResolveCulture("zh-Hant-HK") == "zh-TW" && L.ResolveCulture("zh-CN") == "zh-CN" && L.ResolveCulture("fr-FR") == "en-US", "系统语言映射错误");
                string old = L.Preference;
                try
                {
                    L.Set("en-US"); Assert(new ProbeResult { positionSource = 1 }.SourceName == "Satellite / virtual GNSS", "英文动态定位来源未翻译"); Assert(L.T("应用并核验") == "Apply and verify", "英文资源缺失");
                    L.Set("ja-JP"); Assert(L.T("应用并核验") != "应用并核验" && L.T("外观与语言") != "外观与语言", "日文资源缺失");
                    L.Set("zh-TW"); Assert(L.T("应用并核验") != "应用并核验" && L.T("外观与语言") != "外观与语言", "繁体中文资源缺失");
                    L.Set("zh-CN"); Assert(new ProbeResult { positionSource = 1 }.SourceName == "卫星 / 虚拟 GNSS", "简体中文状态错误");
                    L.Set("auto"); Assert(L.Preference == "auto" && L.Language == L.SystemLanguage, "自动语言未跟随 Windows 用户界面语言");
                }
                finally { L.Set(old); }
            });
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
            Test("地理编码按请求隔离语言并跟随并发切换", () =>
            {
                var handler = new FakeGeocodingHandler { BlockFirstRequest = true };
                using (var service = new GeocodingService(handler))
                {
                    service.SetLanguage("ja-JP");
                    var first = service.SearchAsync("Tokyo Station", "Google", "test-key");
                    Assert(handler.FirstRequestStarted.Task.Wait(TimeSpan.FromSeconds(3)), "第一笔请求没有启动");
                    service.SetLanguage("zh-TW");
                    var second = service.SearchAsync("Osaka Station", "Google", "test-key");
                    handler.ReleaseFirstRequest.TrySetResult(true);
                    Task.WaitAll(first, second);
                    lock (handler.Requests)
                    {
                        Assert(handler.Requests.Count == 2, "并发搜索请求数量不符");
                        Assert(handler.Requests[0].Item2 == GeocodingService.AcceptLanguageFor("ja-JP") && handler.Requests[0].Item1.Query.Contains("language=ja"), "第一笔请求语言未固定为日文：" + handler.Requests[0].Item2 + " / " + handler.Requests[0].Item1.Query);
                        Assert(handler.Requests[1].Item2 == GeocodingService.AcceptLanguageFor("zh-TW") && handler.Requests[1].Item1.Query.Contains("language=zh-TW"), "语言切换后的请求未采用繁体中文：" + handler.Requests[1].Item2 + " / " + handler.Requests[1].Item1.Query);
                    }
                }
            });
            Test("Nominatim 缓存按语言隔离且同语言复用", () =>
            {
                var handler = new FakeGeocodingHandler { ResponseForRequest = (uri, language) => "[{\"display_name\":\"Station\",\"lat\":\"35.681236\",\"lon\":\"139.767125\"}]" };
                using (var service = new GeocodingService(handler))
                {
                    service.SetLanguage("en-US"); service.SearchAsync("Tokyo Station", "OpenStreetMap", "").GetAwaiter().GetResult();
                    service.SetLanguage("ja-JP"); service.SearchAsync("Tokyo Station", "OpenStreetMap", "").GetAwaiter().GetResult();
                    service.SearchAsync("Tokyo Station", "OpenStreetMap", "").GetAwaiter().GetResult();
                    lock (handler.Requests) Assert(handler.Requests.Count == 2 && handler.Requests[0].Item2 == GeocodingService.AcceptLanguageFor("en-US") && handler.Requests[1].Item2 == GeocodingService.AcceptLanguageFor("ja-JP"), "不同语言共用了错误结果，或同语言缓存没有命中：" + handler.Requests.Count + "; " + string.Join(" | ", handler.Requests.Select(item => item.Item2 + " / " + item.Item1)));
                }
            });
            Test("地图短链接拒绝外域跳转并接受安全坐标跳转", () =>
            {
                var hostile = new FakeGeocodingHandler { HttpResponseForRequest = uri => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/collect") } } };
                using (var service = new GeocodingService(hostile))
                {
                    try { service.SearchAsync("https://maps.app.goo.gl/abc", "Google", "").GetAwaiter().GetResult(); throw new Exception("外部重定向没有被拒绝"); }
                    catch (InvalidDataException) { }
                    lock (hostile.Requests) Assert(hostile.Requests.Count == 1, "外域重定向被继续请求");
                }
                var safe = new FakeGeocodingHandler { HttpResponseForRequest = uri => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://www.google.com/maps/@35.681236,139.767125,15z") } } };
                using (var service = new GeocodingService(safe))
                {
                    var found = service.SearchAsync("https://maps.app.goo.gl/abc", "Google", "").GetAwaiter().GetResult();
                    Assert(found.Count == 1 && Math.Abs(found[0].Location.Latitude - 35.681236) < 0.00000001, "安全 Google Maps 坐标跳转没有解析");
                    lock (safe.Requests) Assert(safe.Requests.Count == 1, "已解析的安全跳转被多余请求");
                }
            });
            Test("地理编码拒绝损坏及超限响应", () =>
            {
                foreach (string body in new[] { "{broken", new string('x', 1024 * 1024 + 32) })
                {
                    var handler = new FakeGeocodingHandler { ResponseForRequest = (uri, language) => body };
                    using (var service = new GeocodingService(handler))
                    {
                        try { service.SearchAsync("Tokyo Station", "Google", "test-key").GetAwaiter().GetResult(); throw new Exception("无效响应没有被拒绝"); }
                        catch (InvalidDataException) { }
                    }
                }
            });
            Test("响应正文忽略取消时仍按总期限退出并恢复搜索", () =>
            {
                var body = new UncancellableBodyStream();
                int requests = 0;
                var handler = new FakeGeocodingHandler { HttpResponseForRequest = uri => new HttpResponseMessage(HttpStatusCode.OK) { Content = ++requests == 1 ? (HttpContent)new StreamContent(body) : new StringContent("{\"status\":\"ZERO_RESULTS\"}") } };
                using (var service = new GeocodingService(handler, TimeSpan.FromMilliseconds(150)))
                {
                    var search = service.SearchAsync("Tokyo Station", "Google", "test-key");
                    Assert(Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(3))).GetAwaiter().GetResult() == search, "不响应取消的正文使搜索永久等待");
                    try { search.GetAwaiter().GetResult(); throw new Exception("阻塞正文未报告超时"); }
                    catch (TimeoutException) { }
                    Assert(body.WasDisposed, "超时后没有关闭响应正文");
                    Assert(service.SearchAsync("Osaka Station", "Google", "test-key").GetAwaiter().GetResult().Count == 0, "超时后搜索锁没有释放");
                }
            });
            Test("Nominatim 拒绝错误顶层结构并截短超长名称", () =>
            {
                var malformed = new FakeGeocodingHandler { ResponseForRequest = (uri, language) => "{}" };
                using (var service = new GeocodingService(malformed))
                {
                    try { service.SearchAsync("Tokyo Station", "OpenStreetMap", "").GetAwaiter().GetResult(); throw new Exception("对象形式的错误响应被当成空结果"); }
                    catch (InvalidDataException) { }
                }
                string longName = new string('S', 200);
                var bounded = new FakeGeocodingHandler { ResponseForRequest = (uri, language) => "[{\"display_name\":\"" + longName + "\",\"lat\":\"35.681236\",\"lon\":\"139.767125\"}]" };
                using (var service = new GeocodingService(bounded))
                {
                    var result = service.SearchAsync("Tokyo Station", "OpenStreetMap", "").GetAwaiter().GetResult();
                    Assert(result.Count == 1 && result[0].Title.Length == 120 && result[0].Location.Name.Length == 120, "过长的地点名称未限制在界面显示范围内");
                }
            });
            Test("超大配置有界拒绝且保留原文件", () =>
            {
                string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "oversized-settings");
                var store = new SettingsStore(directory);
                using (var file = new FileStream(store.FilePath, FileMode.Create, FileAccess.Write)) file.SetLength(SettingsStore.MaxSettingsBytes + 1);
                var loaded = store.Load();
                Assert(loaded.Target != null && new FileInfo(store.FilePath).Length == SettingsStore.MaxSettingsBytes + 1, "超大配置没有安全回退或原文件被修改");
                Assert(Directory.GetFiles(directory, "settings.json.invalid-*").Length == 0, "超大原文件被无界复制为备份");
            });
            Test("关闭地理编码服务时在途查询安全释放", () =>
            {
                var handler = new FakeGeocodingHandler { BlockFirstRequest = true };
                var service = new GeocodingService(handler);
                var pending = service.SearchAsync("Tokyo Station", "Google", "test-key");
                Assert(handler.FirstRequestStarted.Task.Wait(TimeSpan.FromSeconds(3)), "在途查询没有启动");
                service.Dispose();
                handler.ReleaseFirstRequest.TrySetResult(true);
                Assert(Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(3))).GetAwaiter().GetResult() == pending, "服务关闭后在途请求未结束");
                try { pending.GetAwaiter().GetResult(); }
                catch (Exception ex)
                {
                    var aggregate = ex as AggregateException;
                    var errors = aggregate == null ? new[] { ex } : aggregate.Flatten().InnerExceptions.ToArray();
                    Assert(!errors.Any(error => error is ObjectDisposedException), "在途请求因释放已销毁资源而失败：" + string.Join(" | ", errors.Select(error => error.GetType().FullName + ": " + error.Message)));
                }
                try { service.SearchAsync("Another Station", "Google", "test-key").GetAwaiter().GetResult(); throw new Exception("已关闭服务仍接受新请求"); }
                catch (ObjectDisposedException) { }
            });
            Write(report, new { createdUtc = DateTime.UtcNow.ToString("o"), passed = failures == 0, count = results.Count, failures, skipped, results });
            return failures == 0 ? 0 : 1;
        }
        public static async Task<int> CheckBackendAsync(string report)
        {
            try
            {
                var state = await Backend.ReadStateAsync(); var probe = await Backend.ProbeAsync();
                double? distance = state.Configured == null ? (double?)null : CoordinateParser.Distance(state.Configured.Latitude, state.Configured.Longitude, probe.latitude, probe.longitude);
                bool passed = state.Healthy && state.Enabled && state.LocationAllowed && LocationVerification.IsWithinConfiguredTolerance(state.Configured, probe);
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
