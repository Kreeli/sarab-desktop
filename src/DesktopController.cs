using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SarabDesktop
{
    public sealed class DesktopController : IDisposable
    {
        public Window Window { get; }
        readonly SettingsStore store;
        readonly GeocodingService geocoder = new GeocodingService();
        AppSettings settings;
        DriverState state;
        ProbeResult probe;
        LocationProfile removedFavorite;
        bool busy, closing, loading, disposed;
        string currentPage = "dashboard";
        readonly bool preview;
        Forms.NotifyIcon tray;
        readonly DispatcherTimer timer;
        readonly List<Action> staticTranslations = new List<Action>();
        PreflightState preflight;
        public DesktopController(bool isPreview = false, string dataDirectory = null, string languageOverride = null)
        {
            preview = isPreview;
            store = new SettingsStore(dataDirectory);
            settings = store.Load();
            L.Set(languageOverride ?? settings.Language);
            if (!preview && languageOverride != null) { settings.Language = L.Language; store.Save(settings); }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SarabDesktop.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            CaptureTranslations(Window, new HashSet<DependencyObject>());
            ApplyStaticTranslations();
            string icon = Path.Combine(Backend.BaseDirectory, "assets", "sarab.ico");
            if (File.Exists(icon)) Window.Icon = BitmapFrame.Create(new Uri(icon));
            Window.SourceInitialized += (s, e) => { try { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle, 20, ref dark, 4); } catch { } };
            store.Logged += message => Window.Dispatcher.BeginInvoke(new Action(() => { if (currentPage == "logs") T<TextBox>("LogsText").Text = store.ReadLog(); }));
            BindEvents();
            LoadSettingsControls();
            T<Button>("ApplyButton").Content = settings.VerifyAfterApply ? L.T("应用并核验") : L.T("应用位置");
            FillForm(settings.Target);
            UpdateFavorites();
            ShowPage("dashboard");
            if (!preview) CreateTray(icon);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += async (s, e) => { UpdateStatus(); if (!busy && Window.IsVisible) await ExecuteAsync(L.T("刷新状态"), RefreshAsync, false); };
            if (!preview) timer.Start();
            Window.Closing += OnClosing;
            Window.PreviewKeyDown += async (s, e) =>
            {
                if (e.Key == Key.F5) { e.Handled = true; await ExecuteAsync(L.T("刷新状态"), RefreshAsync); }
                else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && currentPage == "dashboard") { e.Handled = true; await ExecuteAsync(L.T("应用位置"), ApplyFormAsync); }
                else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; ShowPage("search"); T<TextBox>("SearchInput").Focus(); }
            };
        }
        TControl T<TControl>(string name) where TControl : class => Window.FindName(name) as TControl ?? throw new InvalidOperationException(L.T("缺少界面控件：") + name);
        void On(string name, RoutedEventHandler handler) => T<Button>(name).Click += handler;
        void BindEvents()
        {
            foreach (string name in new[] { "NavDashboard", "NavSearch", "NavFavorites", "NavLogs", "NavSettings", "NavSetup" }) On(name, async (s, e) => { ShowPage(Convert.ToString(((Button)s).Tag)); if (currentPage == "setup") await ExecuteAsync(L.T("检测安装条件"), RefreshSetupAsync); });
            On("RefreshButton", async (s, e) => await ExecuteAsync(L.T("刷新状态"), currentPage == "setup" ? (Func<Task>)RefreshSetupAsync : RefreshAsync));
            On("VerifyButton", async (s, e) => await ExecuteAsync(L.T("核验当前位置"), VerifyAsync));
            On("ApplyButton", async (s, e) => await ExecuteAsync(L.T("应用位置"), ApplyFormAsync));
            On("DisableButton", async (s, e) =>
            {
                if (busy) return;
                if (MessageBox.Show(Window, L.T("暂停后，Windows 可能恢复使用 Wi-Fi、IP 等其他位置来源。\n\n是否暂停虚拟定位？"), L.T("暂停虚拟定位"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                await ExecuteAsync(L.T("暂停虚拟定位"), async () => { await Backend.ElevateAsync("--disable"); probe = null; await RefreshAsync(); store.Log(L.T("已暂停虚拟定位；其他位置来源可能恢复。")); Notice(L.T("已暂停。Windows 可能重新使用其他位置来源。"), true); });
            });
            On("SaveFavoriteButton", (s, e) => Safe(() =>
            {
                var value = ReadForm();
                var old = settings.Favorites.FirstOrDefault(p => p.Name == value.Name);
                if (old != null) { value.Id = old.Id; settings.Favorites[settings.Favorites.IndexOf(old)] = value.Copy(); }
                else { if (settings.Favorites.Count >= 200) throw new InvalidOperationException(L.T("最多支持 200 个收藏位置。")); settings.Favorites.Add(value.Copy()); }
                store.Save(settings); UpdateFavorites(); store.Log(L.T("已保存位置收藏：") + value.Name); Notice(L.T("已保存到位置收藏。"));
            }));
            On("OpenMapButton", (s, e) => Safe(() => { var p = ReadForm(); OpenUrl("https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(p.Coordinates)); }));
            On("GoSearchButton", (s, e) => ShowPage("search"));
            On("OpenLocationSettingsButton", (s, e) => Safe(() => OpenUrl("ms-settings:privacy-location")));
            On("SearchButton", async (s, e) => await ExecuteAsync(L.T("搜索地点"), SearchAsync));
            T<TextBox>("SearchInput").KeyDown += async (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; await ExecuteAsync(L.T("搜索地点"), SearchAsync); } };
            T<ComboBox>("SearchProvider").SelectionChanged += (s, e) =>
            {
                if (loading) return;
                settings.Geocoder = T<ComboBox>("SearchProvider").SelectedIndex == 1 ? "Google" : "OpenStreetMap";
                loading = true; T<ComboBox>("SettingsProvider").SelectedIndex = settings.Geocoder == "Google" ? 1 : 0; loading = false;
                Safe(() => store.Save(settings)); UpdateProviderLabels();
            };
            T<ListBox>("SearchResults").SelectionChanged += (s, e) => T<Button>("UseSearchResultButton").IsEnabled = T<ListBox>("SearchResults").SelectedItem is SearchResult;
            On("UseSearchResultButton", (s, e) => { var result = T<ListBox>("SearchResults").SelectedItem as SearchResult; if (result != null) { FillForm(result.Location); ShowPage("dashboard"); Notice(L.T("地点已载入编辑；点击“应用并核验”才会修改系统位置。")); } });
            On("LoadFavoriteButton", (s, e) => { var p = T<ListBox>("FavoritesList").SelectedItem as LocationProfile; if (p != null) { FillForm(p); ShowPage("dashboard"); Notice(L.T("收藏位置已载入编辑，尚未写入驱动。")); } else Notice(L.T("先选择一个收藏位置。")); });
            On("ApplyFavoriteButton", async (s, e) => { var p = T<ListBox>("FavoritesList").SelectedItem as LocationProfile; if (p != null) { FillForm(p); ShowPage("dashboard"); await ExecuteAsync(L.T("应用收藏位置"), ApplyFormAsync); } else Notice(L.T("先选择一个收藏位置。")); });
            On("DeleteFavoriteButton", (s, e) => Safe(() => { var p = T<ListBox>("FavoritesList").SelectedItem as LocationProfile; if (p == null) return; removedFavorite = p.Copy(); settings.Favorites.Remove(p); store.Save(settings); UpdateFavorites(); T<Button>("UndoFavoriteButton").Visibility = Visibility.Visible; Notice(L.T("已移除收藏，可点击“撤销移除”恢复。")); }));
            On("UndoFavoriteButton", (s, e) => Safe(() => { if (removedFavorite == null) return; settings.Favorites.Add(removedFavorite); removedFavorite = null; store.Save(settings); UpdateFavorites(); T<Button>("UndoFavoriteButton").Visibility = Visibility.Collapsed; Notice(L.T("已恢复收藏。")); }));
            On("ExportFavoritesButton", (s, e) => Safe(ExportFavorites));
            On("ImportFavoritesButton", (s, e) => Safe(ImportFavorites));
            On("CopyLogsButton", (s, e) => Safe(() => { Clipboard.SetText(store.ReadLog()); Notice(L.T("操作记录已复制。记录可能含你设置的坐标。分享前请自行检查。")); }));
            On("OpenDataFolderButton", (s, e) => Safe(() => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + store.DirectoryPath + "\"") { UseShellExecute = true })));
            On("SaveSettingsButton", (s, e) => Safe(SaveSettings));
            T<ComboBox>("LanguageSelector").SelectionChanged += (s, e) =>
            {
                if (loading) return;
                Safe(() =>
                {
                    settings.Language = T<ComboBox>("LanguageSelector").SelectedIndex == 0 ? "zh-CN" : "en-US";
                    L.Set(settings.Language); store.Save(settings); ApplyStaticTranslations(); UpdateStatus(); UpdateProviderLabels(); UpdateKeyStatus(); UpdateTrayLanguage(); UpdateSetupStatus(); ShowPage(currentPage);
                    T<Button>("ApplyButton").Content = L.T(settings.VerifyAfterApply ? L.T("应用并核验") : L.T("应用位置"));
                    Notice(L.T("界面语言已切换，位置与收藏保持不变。"));
                });
            };
            On("OpenUpstreamButton", (s, e) => Safe(() => OpenUrl("https://github.com/TheMRVX/sarab")));
            On("OpenCreditsButton", (s, e) => Safe(() => OpenUrl(Path.Combine(Backend.BaseDirectory, "CREDITS.html"))));
            On("OpenInstallGuideButton", (s, e) => Safe(() => OpenUrl(Path.Combine(Backend.BaseDirectory, "docs", L.Language == "zh-CN" ? "INSTALL.zh-CN.html" : "INSTALL.html"))));
            On("SetupLocationButton", (s, e) => Safe(() => OpenUrl("ms-settings:privacy-location")));
            On("PrepareSystemButton", async (s, e) =>
            {
                if (busy) return;
                if (MessageBox.Show(Window, L.T("此步骤会先检查 Secure Boot 与磁盘保护、备份启动配置，然后开启测试签名。它会放宽驱动签名限制，需手动重启后生效。不会关闭 Secure Boot、暂停磁盘保护或自动重启。若使用 BitLocker，请先备份恢复密钥并按指南手动暂停保护。\n\n已完成上述准备，是否继续？"), L.T("准备 Windows 测试模式"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                await ExecuteAsync(L.T("准备 Windows 测试模式"), async () => { await Backend.ElevateAsync("--prepare-system"); await RefreshSetupAsync(); Notice(L.T("准备步骤完成。如果测试签名尚未在本次启动生效，请手动重启后重新打开安装引导。")); });
            });
            On("InstallDriverButton", async (s, e) =>
            {
                if (busy) return;
                if (MessageBox.Show(Window, L.T("将安装 TheMRVX 的 Sarab v1.0.0 虚拟 GNSS 驱动，并将其固定测试证书加入本机 Root 与 TrustedPublisher。安装后虚拟定位保持暂停，需自行设置坐标并应用。\n\n是否安装？"), L.T("安装原版 Sarab 驱动"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                await ExecuteAsync(L.T("安装原版 Sarab 驱动"), async () => { await Backend.ElevateAsync("--install-driver"); await RefreshAsync(); await RefreshSetupAsync(); Notice(L.T("驱动已安装且设备健康。请确认 Windows 位置权限，再到概览填写坐标并应用。")); });
            });
            On("RemoveDriverButton", async (s, e) =>
            {
                if (busy) return;
                if (MessageBox.Show(Window, L.T("将移除本机所有 Root\\SarabGnss 虚拟设备及确认属于 Sarab 的驱动包。Windows 可能恢复使用真实位置来源。测试证书和全局测试签名设置将保留，清理说明见安装指南。\n\n是否卸载驱动？"), L.T("卸载 Sarab 驱动"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                await ExecuteAsync(L.T("卸载 Sarab 驱动"), async () => { await Backend.ElevateAsync("--remove-driver"); probe = null; await RefreshAsync(); await RefreshSetupAsync(); Notice(L.T("驱动已移除。证书及测试模式清理步骤见安装指南。")); });
            });
            On("ClearGoogleKeyButton", (s, e) => Safe(() => { settings.GoogleKeyProtected = null; T<System.Windows.Controls.PasswordBox>("GoogleKeyInput").Clear(); store.Save(settings); UpdateKeyStatus(); Notice(L.T("已清除保存的 Google API Key。")); }));
            T<TextBox>("LatitudeInput").TextChanged += (s, e) => UpdateMapCaption();
            T<TextBox>("LongitudeInput").TextChanged += (s, e) => UpdateMapCaption();
        }
        public async Task InitializeAsync()
        {
            await ExecuteAsync(L.T("读取驱动状态"), RefreshAsync, false);
            if (settings.VerifyOnStartup && state != null && state.Healthy && state.Enabled && state.LocationAllowed) await ExecuteAsync(L.T("启动核验"), VerifyAsync, false);
            if (state != null && !state.Installed) { ShowPage("setup"); await ExecuteAsync(L.T("检测安装条件"), RefreshSetupAsync); }
        }
        async Task RefreshSetupAsync() { preflight = await Installation.ReadPreflightAsync(); UpdateSetupStatus(); }
        public async Task OpenSetupAsync() { ShowPage("setup"); await ExecuteAsync(L.T("检测安装条件"), RefreshSetupAsync); }
        void UpdateSetupStatus()
        {
            if (preflight == null) return;
            T<TextBlock>("SetupOsText").Text = (preflight.IsAmd64 ? "x64" : L.T("不支持的架构")) + " · Windows build " + preflight.WindowsBuild;
            T<TextBlock>("SetupBootText").Text = preflight.SecureBoot == null ? L.T("未知，请检查 UEFI 设置") : preflight.SecureBoot == true ? L.T("已开启，准备测试模式前需手动关闭") : L.T("已关闭");
            T<TextBlock>("SetupSigningText").Text = L.T(preflight.TestSigning ? L.T("本次启动已生效") : L.T("尚未生效；准备后需手动重启"));
            T<TextBlock>("SetupDriverText").Text = preflight.Driver.Healthy ? L.T("已安装，设备健康") : preflight.Driver.Installed ? L.T("设备需要修复") + " (" + preflight.Driver.ProblemCode + ")" : L.T("未安装");
            T<TextBlock>("SetupPermissionText").Text = L.T(preflight.Driver.LocationAllowed ? L.T("允许") : L.T("关闭，请在 Windows 设置中手动允许"));
            T<Button>("PrepareSystemButton").IsEnabled = !busy && !preflight.TestSigning;
            T<Button>("InstallDriverButton").IsEnabled = !busy && preflight.IsAmd64 && preflight.WindowsBuild >= 17763 && preflight.TestSigning && !preflight.Driver.Healthy;
            T<Button>("RemoveDriverButton").IsEnabled = !busy && preflight.Driver.Installed;
        }
        async Task RefreshAsync()
        {
            var next = await Backend.ReadStateAsync();
            if (state?.Configured?.Coordinates != next.Configured?.Coordinates || state?.Enabled != next.Enabled || state?.LocationAllowed != next.LocationAllowed) probe = null;
            state = next;
            UpdateStatus();
        }
        async Task VerifyAsync()
        {
            await RefreshAsync();
            probe = null; UpdateStatus();
            if (state == null || !state.LocationAllowed) throw new InvalidOperationException(L.T("Windows 位置总开关关闭，请先在 Windows 设置中允许位置访问。"));
            probe = await Backend.ProbeAsync();
            UpdateStatus();
            if (!Verified())
            {
                store.Log(L.T("定位核验未通过：来源 ") + probe.SourceName + L.T("，未确认目标位置生效。"));
                Notice(L.T("核验未通过：Windows 当前返回“") + probe.SourceName + L.T("”，或坐标与驱动配置不符。"), true);
            }
            else { store.Log(L.T("实际定位核验通过：") + probe.latitude.ToString("F7", CultureInfo.InvariantCulture) + ", " + probe.longitude.ToString("F7", CultureInfo.InvariantCulture) + L.T("；来源 Satellite。")); Notice(L.T("核验通过：Windows 正在返回驱动设定的虚拟坐标。")); }
        }
        async Task ApplyFormAsync()
        {
            var p = ReadForm();
            await Backend.ElevateAsync(Backend.ApplyArguments(p));
            settings.Target = p.Copy(); settings.LastAppliedUtc = DateTime.UtcNow.ToString("o"); store.Save(settings); probe = null;
            store.Log(L.T("已写入虚拟位置：") + p.Coordinates + L.T("；漂移") + (p.Drift ? L.T("开启") : L.T("关闭")) + "。");
            await RefreshAsync();
            if (settings.VerifyAfterApply) await VerifyAsync();
            else Notice(L.T("配置已写入；尚未核验 Windows 的实际返回结果。"));
        }
        bool Verified()
        {
            if (state?.Configured == null || !state.Healthy || !state.Enabled || !state.LocationAllowed || probe == null || probe.positionSource != 1) return false;
            double tolerance = state.Configured.Drift ? Math.Max(25, state.Configured.DriftRadius * 3 + probe.accuracyMeters) : 25;
            return CoordinateParser.Distance(probe.latitude, probe.longitude, state.Configured.Latitude, state.Configured.Longitude) <= tolerance;
        }
        void UpdateStatus()
        {
            if (state == null) return;
            bool fresh = probe != null && (DateTime.Now - probe.CapturedAt).TotalSeconds < 60;
            bool verified = fresh && Verified();
            string status = !state.Installed ? L.T("未找到虚拟 GNSS 驱动") : !state.Healthy ? L.T("驱动需要检查") : !state.Enabled ? L.T("虚拟定位已暂停") : !state.LocationAllowed ? L.T("Windows 定位权限已关闭") : verified ? L.T("系统虚拟定位已核验") : L.T("虚拟位置已配置 · 待核验");
            T<TextBlock>("HeroStatus").Text = status;
            T<TextBlock>("HeroCoordinates").Text = state.Configured?.Coordinates ?? L.T("驱动没有有效坐标");
            T<TextBlock>("HeroDetail").Text = verified ? L.T("Windows 实际读数与驱动坐标匹配") : L.T("以实际核验为准，不将配置开关当作生效证明");
            T<TextBlock>("SidebarStatus").Text = verified ? L.T("虚拟定位已核验") : state.Healthy && state.Enabled ? L.T("虚拟位置已配置") : state.Installed ? L.T("虚拟定位已暂停") : L.T("驱动未就绪");
            T<System.Windows.Shapes.Ellipse>("SidebarDot").Fill = Brush(verified ? "#91D9B0" : "#CAB376");
            T<TextBlock>("DriverStatusText").Text = state.Healthy ? L.T("正常") : state.Installed ? L.T("错误 ") + state.ProblemCode : L.T("未安装");
            T<TextBlock>("DriverStatusText").Foreground = Brush(state.Healthy ? "#91D9B0" : "#E5A3AD");
            T<TextBlock>("PermissionStatusText").Text = state.LocationAllowed ? L.T("允许") : L.T("关闭");
            T<TextBlock>("SigningStatusText").Text = state.TestSigning ? L.T("已开启") : L.T("未开启");
            T<Button>("DisableButton").IsEnabled = !busy && state.Enabled;
            if (probe != null)
            {
                T<TextBlock>("ActualSource").Text = probe.SourceName;
                T<TextBlock>("ActualSource").Foreground = Brush(Verified() ? "#91D9B0" : "#E5A3AD");
                T<TextBlock>("ActualCoordinates").Text = probe.latitude.ToString("F7", CultureInfo.InvariantCulture) + "\n" + probe.longitude.ToString("F7", CultureInfo.InvariantCulture);
                T<TextBlock>("ActualAccuracy").Text = L.T("报告精度  ") + probe.accuracyMeters.ToString("F2") + L.T(" 米");
                T<TextBlock>("ActualDistance").Text = state.Configured == null ? L.T("距驱动配置  —") : L.T("距驱动配置  ") + CoordinateParser.Distance(probe.latitude, probe.longitude, state.Configured.Latitude, state.Configured.Longitude).ToString("F2") + L.T(" 米");
                T<TextBlock>("ActualTime").Text = L.T("上次核验 ") + probe.CapturedAt.ToString("HH:mm:ss") + (fresh ? "" : L.T(" · 读数已过期，请重新核验"));
            }
            else
            {
                T<TextBlock>("ActualSource").Text = L.T("尚未核验"); T<TextBlock>("ActualSource").Foreground = Brush("#C5B5F3");
                T<TextBlock>("ActualCoordinates").Text = L.T("点击核验，读取 Windows 实际位置");
                T<TextBlock>("ActualAccuracy").Text = L.T("报告精度  —"); T<TextBlock>("ActualDistance").Text = L.T("距驱动配置  —"); T<TextBlock>("ActualTime").Text = L.T("尚无当前配置的采样记录");
            }
        }
        async Task SearchAsync()
        {
            string input = T<TextBox>("SearchInput").Text;
            var results = await geocoder.SearchAsync(input, settings.Geocoder, SettingsStore.Unprotect(settings.GoogleKeyProtected));
            T<ListBox>("SearchResults").ItemsSource = results;
            T<ListBox>("SearchResults").SelectedIndex = results.Count > 0 ? 0 : -1;
            T<TextBlock>("SearchHint").Text = results.Count == 0 ? L.T("没有找到结果。请补充城市、国家或复制地图上的坐标。") : L.T("找到 ") + results.Count + L.T(" 个结果。选中后载入编辑，再决定是否应用。");
            store.Log(L.T("地点查询完成：") + settings.Geocoder + L.T("，结果 ") + results.Count + L.T(" 个。"));
        }
        public void ShowPage(string page)
        {
            currentPage = page;
            string[] pages = { "dashboard", "search", "favorites", "logs", "settings", "setup" };
            string[] panels = { "DashboardPage", "SearchPage", "FavoritesPage", "LogsPage", "SettingsPage", "SetupPage" };
            string[] navs = { "NavDashboard", "NavSearch", "NavFavorites", "NavLogs", "NavSettings", "NavSetup" };
            string[] titles = { L.T("位置控制台"), L.T("地点搜索"), L.T("位置收藏"), L.T("操作记录"), L.T("设置"), L.T("安装与修复") };
            string[] subtitles = { L.T("让应用读取你设定的地点"), L.T("地址、地图链接与度分秒，都能载入为目标坐标"), L.T("为常用虚拟地点建立自己的快捷入口"), L.T("保存在本机的配置与核验记录"), L.T("启动方式、后台行为与地址搜索服务"), L.T("逐步检查系统条件，安装原版 Sarab 驱动") };
            for (int i = 0; i < pages.Length; i++)
            {
                bool active = page == pages[i];
                T<FrameworkElement>(panels[i]).Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                T<Button>(navs[i]).Background = Brush(active ? "#41364F" : "Transparent");
                if (active) { T<TextBlock>("PageTitle").Text = titles[i]; T<TextBlock>("PageSubtitle").Text = subtitles[i]; }
            }
            if (page == "logs") T<TextBox>("LogsText").Text = store.ReadLog();
        }
        void FillForm(LocationProfile p)
        {
            T<TextBox>("NameInput").Text = p.Name;
            T<TextBox>("LatitudeInput").Text = p.Latitude.ToString("F10", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            T<TextBox>("LongitudeInput").Text = p.Longitude.ToString("F10", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            T<TextBox>("AltitudeInput").Text = p.Altitude.ToString(CultureInfo.InvariantCulture);
            T<TextBox>("AccuracyInput").Text = p.Accuracy.ToString(CultureInfo.InvariantCulture);
            T<CheckBox>("DriftToggle").IsChecked = p.Drift;
            T<TextBox>("DriftRadiusInput").Text = p.DriftRadius.ToString(CultureInfo.InvariantCulture);
            UpdateMapCaption();
        }
        LocationProfile ReadForm()
        {
            var p = new LocationProfile { Name = T<TextBox>("NameInput").Text.Trim(), Latitude = ReadNumber("LatitudeInput", L.T("纬度")), Longitude = ReadNumber("LongitudeInput", L.T("经度")), Altitude = ReadNumber("AltitudeInput", L.T("海拔")), Accuracy = ReadNumber("AccuracyInput", L.T("精度")), Drift = T<CheckBox>("DriftToggle").IsChecked == true, DriftRadius = ReadNumber("DriftRadiusInput", L.T("漂移半径")) };
            p.Validate(); return p;
        }
        double ReadNumber(string name, string label)
        {
            double n;
            if (!double.TryParse(T<TextBox>(name).Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) throw new ArgumentException(label + L.T("请输入有效数字，小数点使用英文句点。"));
            return n;
        }
        void UpdateMapCaption()
        {
            var caption = Window.FindName("MapCaption") as TextBlock;
            if (caption != null) caption.Text = T<TextBox>("LatitudeInput").Text + ", " + T<TextBox>("LongitudeInput").Text;
        }
        void UpdateFavorites()
        {
            T<ListBox>("FavoritesList").ItemsSource = null;
            T<ListBox>("FavoritesList").ItemsSource = settings.Favorites;
            if (settings.Favorites.Count > 0) T<ListBox>("FavoritesList").SelectedIndex = 0;
        }
        void LoadSettingsControls()
        {
            loading = true;
            T<ComboBox>("LanguageSelector").SelectedIndex = L.Language == "zh-CN" ? 0 : 1;
            T<CheckBox>("StartupToggle").IsChecked = StartupRegistration.Enabled;
            T<CheckBox>("CloseToTrayToggle").IsChecked = settings.CloseToTray;
            T<CheckBox>("VerifyStartupToggle").IsChecked = settings.VerifyOnStartup;
            T<CheckBox>("VerifyApplyToggle").IsChecked = settings.VerifyAfterApply;
            T<ComboBox>("SearchProvider").SelectedIndex = settings.Geocoder == "Google" ? 1 : 0;
            T<ComboBox>("SettingsProvider").SelectedIndex = settings.Geocoder == "Google" ? 1 : 0;
            loading = false; UpdateProviderLabels(); UpdateKeyStatus();
        }
        void UpdateProviderLabels()
        {
            bool google = settings.Geocoder == "Google";
            T<TextBlock>("SearchPrivacyText").Text = google ? L.T("地址将发送至 Google Geocoding。需要你自己的 API Key；只在点击搜索时查询。") : L.T("地址将发送至 OpenStreetMap / Nominatim。仅在点击搜索时查询；坐标可离线解析。");
            T<TextBlock>("SearchAttribution").Text = google ? L.T("地址结果由 Google 提供 · Google Maps 链接无需 API Key") : "© OpenStreetMap contributors · Nominatim";
        }
        void UpdateKeyStatus() => T<TextBlock>("GoogleKeyStatus").Text = string.IsNullOrEmpty(SettingsStore.Unprotect(settings.GoogleKeyProtected)) ? L.T("未配置 · Key 将按当前 Windows 用户加密保存") : L.T("已配置 · Key 已加密保存（当前 Windows 用户）");
        void SaveSettings()
        {
            bool desiredStartup = T<CheckBox>("StartupToggle").IsChecked == true;
            StartupRegistration.Set(desiredStartup);
            settings.CloseToTray = T<CheckBox>("CloseToTrayToggle").IsChecked == true;
            settings.VerifyOnStartup = T<CheckBox>("VerifyStartupToggle").IsChecked == true;
            settings.VerifyAfterApply = T<CheckBox>("VerifyApplyToggle").IsChecked == true;
            settings.Geocoder = T<ComboBox>("SettingsProvider").SelectedIndex == 1 ? "Google" : "OpenStreetMap";
            string key = T<System.Windows.Controls.PasswordBox>("GoogleKeyInput").Password.Trim();
            if (!string.IsNullOrEmpty(key)) settings.GoogleKeyProtected = SettingsStore.Protect(key);
            T<System.Windows.Controls.PasswordBox>("GoogleKeyInput").Clear();
            store.Save(settings); LoadSettingsControls();
            T<Button>("ApplyButton").Content = settings.VerifyAfterApply ? L.T("应用并核验") : L.T("应用位置");
            store.Log(L.T("设置已保存；登录自启动") + (desiredStartup ? L.T("开启") : L.T("关闭")) + "。"); Notice(L.T("设置已保存。"));
        }
        public sealed class ProfileBundle { public int SchemaVersion { get; set; } = 1; public List<LocationProfile> Favorites { get; set; } }
        void ExportFavorites()
        {
            var dialog = new SaveFileDialog { FileName = L.T("Sarab-位置收藏.json"), Filter = L.T("JSON 文件|*.json") };
            if (dialog.ShowDialog(Window) != true) return;
            File.WriteAllText(dialog.FileName, Json.To(new ProfileBundle { Favorites = settings.Favorites }), new System.Text.UTF8Encoding(false));
            Notice(L.T("收藏已导出，不包含 Google API Key 或开机启动配置。"));
        }
        void ImportFavorites()
        {
            var dialog = new OpenFileDialog { Filter = L.T("JSON 文件|*.json"), CheckFileExists = true };
            if (dialog.ShowDialog(Window) != true) return;
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException(L.T("收藏文件过大，最多支持 1 MB。"));
            var bundle = Json.From<ProfileBundle>(File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8));
            if (bundle == null || bundle.SchemaVersion != 1 || bundle.Favorites == null) throw new InvalidDataException(L.T("不是受支持的位置收藏文件。"));
            foreach (var p in bundle.Favorites) { if (p == null) throw new InvalidDataException(L.T("收藏中含空记录。")); p.Validate(); }
            var merged = settings.Favorites.Select(p => p.Copy()).ToList();
            foreach (var p in bundle.Favorites) if (!merged.Any(old => old.Name == p.Name && old.Coordinates == p.Coordinates)) { var copy = p.Copy(); copy.Id = Guid.NewGuid().ToString("N"); merged.Add(copy); }
            if (merged.Count > 200) throw new InvalidDataException(L.T("合并后超过 200 个位置，请减少导入记录。"));
            settings.Favorites = merged; store.Save(settings); UpdateFavorites(); store.Log(L.T("位置收藏已导入，共 ") + merged.Count + L.T(" 个。")); Notice(L.T("收藏已合并导入，没有修改当前虚拟位置。"));
        }
        async Task ExecuteAsync(string action, Func<Task> work, bool showProgress = true)
        {
            if (busy) return;
            busy = true; SetBusy(true);
            if (showProgress) Notice(action + "…");
            try { await work(); }
            catch (Exception ex) { string message = ex.Message; string secret = SettingsStore.Unprotect(settings.GoogleKeyProtected); if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, L.T("[已隐藏 Key]")); store.Log(action + L.T("未完成：") + message); Notice(message, true); }
            finally { busy = false; SetBusy(false); UpdateStatus(); }
        }
        void SetBusy(bool value)
        {
            foreach (string name in new[] { "ApplyButton", "VerifyButton", "RefreshButton", "SearchButton", "ApplyFavoriteButton", "DisableButton", "PrepareSystemButton", "InstallDriverButton", "RemoveDriverButton" }) T<Button>(name).IsEnabled = !value;
            UpdateSetupStatus();
        }
        void Safe(Action work) { try { work(); } catch (Exception ex) { store.Log(L.T("操作未完成：") + ex.Message); Notice(ex.Message, true); } }
        void Notice(string text, bool warning = false)
        {
            T<Border>("NoticeBar").Visibility = Visibility.Visible;
            T<Border>("NoticeBar").Background = Brush(warning ? "#462C36" : "#302A42");
            T<TextBlock>("NoticeText").Foreground = Brush(warning ? "#F2C4CB" : "#DFD5F2");
            T<TextBlock>("NoticeText").Text = text;
        }
        static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color);
        void CaptureTranslations(DependencyObject element, HashSet<DependencyObject> seen)
        {
            if (!seen.Add(element)) return;
            var framework = element as FrameworkElement;
            var dynamicNames = new[] { "PageTitle", "PageSubtitle", "HeroStatus", "HeroCoordinates", "HeroDetail", "SidebarStatus", "DriverStatusText", "PermissionStatusText", "SigningStatusText", "ActualSource", "ActualCoordinates", "ActualAccuracy", "ActualDistance", "ActualTime", "MapCaption", "SearchPrivacyText", "SearchAttribution", "SearchHint", "GoogleKeyStatus", "NoticeText", "ApplyButton" };
            if (framework == null || !dynamicNames.Contains(framework.Name))
            {
                foreach (var property in new[] { TextBlock.TextProperty, ContentControl.ContentProperty, HeaderedContentControl.HeaderProperty, FrameworkElement.ToolTipProperty })
                {
                    if (BindingOperations.IsDataBound(element, property)) continue;
                    string source = element.GetValue(property) as string;
                    if (source != null && source.Any(c => c >= '\u3400' && c <= '\u9fff')) { var captured = element; var capturedProperty = property; staticTranslations.Add(() => captured.SetValue(capturedProperty, L.T(source))); }
                }
            }
            foreach (var child in LogicalTreeHelper.GetChildren(element)) if (child is DependencyObject) CaptureTranslations((DependencyObject)child, seen);
        }
        void ApplyStaticTranslations() { foreach (var translate in staticTranslations) translate(); }
        void UpdateTrayLanguage()
        {
            if (tray == null) return;
            tray.Text = L.T("Sarab Desktop · 虚拟位置");
            tray.ContextMenuStrip.Items[0].Text = L.T("打开控制台");
            tray.ContextMenuStrip.Items[1].Text = L.T("核验当前位置");
            tray.ContextMenuStrip.Items[3].Text = L.T("退出上位机（保留虚拟定位）");
        }
        static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        void CreateTray(string icon)
        {
            tray = new Forms.NotifyIcon { Text = L.T("Sarab Desktop · 虚拟位置"), Visible = true, Icon = File.Exists(icon) ? new System.Drawing.Icon(icon) : System.Drawing.SystemIcons.Application };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(L.T("打开控制台"), null, (s, e) => ShowWindow());
            menu.Items.Add(L.T("核验当前位置"), null, async (s, e) => { ShowWindow(); await ExecuteAsync(L.T("核验当前位置"), VerifyAsync); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(L.T("退出上位机（保留虚拟定位）"), null, (s, e) => Exit());
            tray.ContextMenuStrip = menu; tray.DoubleClick += (s, e) => ShowWindow();
        }
        public void ShowWindow() { Window.Show(); if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal; Window.Activate(); }
        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!closing && !preview && settings.CloseToTray) { e.Cancel = true; Window.Hide(); }
            else if (!preview) { Dispose(); Application.Current.Shutdown(); }
        }
        public void Exit() { closing = true; Window.Close(); }
        public void Dispose() { if (disposed) return; disposed = true; timer?.Stop(); if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } geocoder.Dispose(); }
        public async Task RenderAsync(string directory)
        {
            Directory.CreateDirectory(directory);
            string oldError = Path.Combine(directory, "render-error.txt");
            if (File.Exists(oldError)) File.Delete(oldError);
            Window.Left = -20000; Window.Top = -20000; Window.ShowInTaskbar = false; Window.Show();
            await InitializeAsync();
            await RefreshSetupAsync();
            foreach (string page in new[] { "dashboard", "search", "favorites", "settings", "setup", "logs" })
            {
                ShowPage(page); Window.UpdateLayout();
                await Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var root = T<FrameworkElement>("RootSurface");
                var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var output = File.Create(Path.Combine(directory, page + ".png"))) encoder.Save(output);
            }
            closing = true; Window.Close();
        }
        public async Task UiSmokeAsync(string directory)
        {
            Directory.CreateDirectory(directory);
            Window.Left = -20000; Window.Top = -20000; Window.ShowInTaskbar = false; Window.Show();
            var results = new List<string>();
            await InitializeAsync();
            if (!Verified()) throw new InvalidDataException(L.T("界面未能读回并核验当前虚拟位置。"));
            results.Add(L.T("启动时实际定位核验"));
            T<TextBox>("SearchInput").Text = "W0°7'40.08\" N51°30'26.64\"";
            ShowPage("search"); await SearchAsync();
            if (T<ListBox>("SearchResults").Items.Count != 1) throw new InvalidDataException(L.T("界面坐标查询失败。"));
            T<Button>("UseSearchResultButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (CoordinateParser.Distance(ReadForm().Latitude, ReadForm().Longitude, 51.5074, -0.1278) > 0.01) throw new InvalidDataException(L.T("载入编辑坐标不匹配。"));
            results.Add(L.T("坐标搜索 → 选择结果 → 载入编辑"));
            T<TextBox>("NameInput").Text = L.T("界面测试收藏");
            int count = settings.Favorites.Count;
            T<Button>("SaveFavoriteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (settings.Favorites.Count != count + 1) throw new InvalidDataException(L.T("保存收藏失败。"));
            T<ListBox>("FavoritesList").SelectedItem = settings.Favorites.Last();
            T<Button>("DeleteFavoriteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (settings.Favorites.Count != count) throw new InvalidDataException(L.T("移除收藏失败。"));
            T<Button>("UndoFavoriteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (settings.Favorites.Count != count + 1 || store.Load().Favorites.Count != count + 1) throw new InvalidDataException(L.T("撤销收藏或持久化失败。"));
            results.Add(L.T("收藏保存、移除、撤销与磁盘读回"));
            T<TextBox>("LatitudeInput").Text = "91";
            bool rejected = false; try { ReadForm(); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidDataException(L.T("界面接受了越界纬度。"));
            FillForm(LocationProfile.Example()); results.Add(L.T("界面拦截无效坐标"));
            foreach (string page in new[] { "dashboard", "search", "favorites", "logs", "settings" }) { ShowPage(page); Window.UpdateLayout(); }
            results.Add(L.T("五页导航与布局"));
            string inputBeforeSwitch = T<TextBox>("LatitudeInput").Text + "|" + T<TextBox>("NameInput").Text;
            T<ComboBox>("LanguageSelector").SelectedIndex = 0;
            T<ComboBox>("LanguageSelector").SelectedIndex = 1;
            ShowPage("settings");
            if (T<TextBlock>("PageTitle").Text != "Settings" || Convert.ToString(T<Button>("ApplyButton").Content) != "Apply and verify" || store.Load().Language != "en-US") throw new InvalidDataException("Live language switch or persistence failed.");
            if (inputBeforeSwitch != T<TextBox>("LatitudeInput").Text + "|" + T<TextBox>("NameInput").Text) throw new InvalidDataException("Language switch changed editor input.");
            T<ComboBox>("LanguageSelector").SelectedIndex = 0;
            if (T<TextBlock>("PageTitle").Text != "设置" || store.Load().Language != "zh-CN") throw new InvalidDataException("Chinese switch failed.");
            results.Add("Live English / Chinese switch, persistence and unchanged editor input");
            File.WriteAllText(Path.Combine(directory, "ui-smoke.json"), Json.To(new { passed = true, results }), new System.Text.UTF8Encoding(false));
            closing = true; Window.Close();
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
