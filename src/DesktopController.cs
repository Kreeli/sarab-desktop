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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SarabDesktop
{
    public sealed class DesktopController : IDisposable
    {
        const double SidebarCompactThreshold = 188;
        const string AccessDeniedNoticeSource = "Windows 拒绝访问此操作。请确认当前用户具备所需权限，然后重试。";
        static readonly Color[] NoticeSpectrum = { Color.FromRgb(244, 65, 103), Color.FromRgb(179, 86, 230), Color.FromRgb(78, 132, 244), Color.FromRgb(53, 202, 219), Color.FromRgb(75, 196, 126), Color.FromRgb(240, 174, 66) };
        static readonly string[] SidebarNavNames = { "NavDashboard", "NavSearch", "NavFavorites", "NavSetup", "NavLogs", "NavSettings" };
        static readonly string[] SidebarIconNames = { "NavDashboardIcon", "NavSearchIcon", "NavFavoritesIcon", "NavSetupIcon", "NavLogsIcon", "NavSettingsIcon" };
        static readonly string[] SidebarLabelNames = { "NavDashboardLabel", "NavSearchLabel", "NavFavoritesLabel", "NavSetupLabel", "NavLogsLabel", "NavSettingsLabel" };
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
        readonly Dictionary<FrameworkElement, double> baseFontSizes = new Dictionary<FrameworkElement, double>();
        PreflightState preflight;
        bool initialized, systemEventsSubscribed, lastNoticeWarning, systemBackdropApplied;
        bool sidebarLayoutInitialized, sidebarCompact, noticeAwardMode;
        string lastNoticeText, lastNoticeSource;
        Window activeNoticeCertificate;
        Grid activeNoticeCertificateSurface;
        Border activeNoticeCertificateFrame, activeNoticeCertificateInner, activeNoticeCertificateHeadlinePanel, activeNoticeCertificateDetailsPanel;
        TextBlock activeNoticeCertificateHeadline;
        TextBlock activeNoticeCertificateDetails;
        Button activeNoticeCertificateCloseButton;
        MediaPlayer noticeMusicPlayer;
        string activeNoticeMusicPath;
        readonly HashSet<string> noticeMusicTempFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] noticeMusicResources;
        int previousNoticeMusicIndex = -1;
        bool noticeMusicRequested;
        TaskCompletionSource<bool> noticeMusicSmokeOpened;
        readonly Random noticeMusicRandom = new Random();
        public DesktopController(bool isPreview = false, string dataDirectory = null, string languageOverride = null)
        {
            preview = isPreview;
            store = new SettingsStore(dataDirectory);
            settings = store.Load();
            L.Set(languageOverride ?? settings.Language);
            if (!preview && languageOverride != null) { settings.Language = L.Preference; if (!store.IsReadOnlyDueToNewerSchema) store.Save(settings); }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SarabDesktop.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            CaptureTranslations(Window, new HashSet<DependencyObject>());
            CaptureBaseFontSizes(Window, new HashSet<DependencyObject>());
            ApplyStaticTranslations();
            Appearance.Apply(Window, settings);
            ApplyFontScale();
            string icon = Path.Combine(Backend.BaseDirectory, "assets", "sarab.ico");
            if (File.Exists(icon)) Window.Icon = BitmapFrame.Create(new Uri(icon));
            Window.SourceInitialized += (s, e) => SetTitleBarTheme();
            store.Logged += message => Window.Dispatcher.BeginInvoke(new Action(() => { if (currentPage == "logs") T<TextBox>("LogsText").Text = store.ReadLog(); }));
            BindEvents();
            LoadSettingsControls();
            T<Button>("ApplyButton").Content = settings.VerifyAfterApply ? L.T("应用并核验") : L.T("应用位置");
            FillForm(settings.Target);
            UpdateFavorites();
            ShowPage("dashboard");
            if (store.IsReadOnlyDueToNewerSchema) Notice(L.T("检测到较新版本的配置，当前版本将以只读方式打开以保护原配置。"), true);
            if (!preview) CreateTray(icon);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += async (s, e) => { UpdateStatus(); if (!busy && Window.IsVisible) await ExecuteAsync(L.T("刷新状态"), RefreshAsync, false); };
            if (!preview) timer.Start();
            if (!preview) { SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged; systemEventsSubscribed = true; }
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
            InitializeSidebar();
            T<Border>("NoticeBar").PreviewMouseLeftButtonDown += OnNoticeBarMouseDown;
            T<Border>("NoticeBar").KeyDown += OnNoticeBarKeyDown;
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
                store.Save(settings); removedFavorite = null; T<Button>("UndoFavoriteButton").Visibility = Visibility.Collapsed; UpdateFavorites(); store.Log(L.T("已保存位置收藏：") + value.Name); Notice(L.T("已保存到位置收藏。"));
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
            On("UndoFavoriteButton", (s, e) => Safe(() =>
            {
                if (removedFavorite == null) return;
                if (settings.Favorites.Count >= 200) { Notice(L.T("最多支持 200 个收藏位置。"), true); return; }
                if (settings.Favorites.Any(p => p.Id == removedFavorite.Id || p.Name == removedFavorite.Name)) { Notice(L.T("同名收藏已存在，无法撤销这次移除。"), true); return; }
                settings.Favorites.Add(removedFavorite); store.Save(settings); removedFavorite = null; UpdateFavorites(); T<Button>("UndoFavoriteButton").Visibility = Visibility.Collapsed; Notice(L.T("已恢复收藏。"));
            }));
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
                    var item = T<ComboBox>("LanguageSelector").SelectedItem as ComboBoxItem;
                    settings.Language = Convert.ToString(item?.Tag) ?? "auto";
                    L.Set(settings.Language); store.Save(settings); ApplyLocale();
                    Notice(L.T("界面语言已切换，位置与收藏保持不变。"));
                });
            };
            T<CheckBox>("SystemThemeToggle").Checked += (s, e) => UpdateThemeChoice();
            T<CheckBox>("SystemThemeToggle").Unchecked += (s, e) => UpdateThemeChoice();
            T<CheckBox>("NightModeToggle").Checked += (s, e) => UpdateThemeChoice();
            T<CheckBox>("NightModeToggle").Unchecked += (s, e) => UpdateThemeChoice();
            T<ComboBox>("AccentSelector").SelectionChanged += (s, e) => Safe(SaveAppearanceSettings);
            T<Slider>("FontScaleSlider").ValueChanged += (s, e) => { if (!loading) Safe(() => { settings.FontScale = SnapFontScale(e.NewValue); SaveAppearanceSettings(); }); };
            On("ResetFontScaleButton", (s, e) => T<Slider>("FontScaleSlider").Value = 100);
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
            T<ListBox>("FavoritesList").SelectionChanged += (s, e) => UpdateFavoriteActions();
        }
        void InitializeSidebar()
        {
            var root = T<Grid>("RootSurface");
            settings.SidebarWidth = AppSettings.NormalizeSidebarWidth(settings.SidebarWidth);
            root.ColumnDefinitions[0].Width = new GridLength(settings.SidebarWidth);
            T<Border>("SidebarHost").SizeChanged += (s, e) => ApplySidebarLayout(e.NewSize.Width);
            T<GridSplitter>("SidebarSplitter").DragCompleted += OnSidebarDragCompleted;
            T<GridSplitter>("SidebarSplitter").PreviewKeyDown += OnSidebarSplitterKeyDown;
            foreach (string name in SidebarNavNames)
            {
                var button = T<Button>(name);
                button.GotKeyboardFocus += OnSidebarNavGotKeyboardFocus;
                button.LostKeyboardFocus += OnSidebarNavLostKeyboardFocus;
            }
            ApplySidebarLayout(settings.SidebarWidth, true);
        }
        void ApplySidebarLayout(double width, bool force = false)
        {
            if (width <= 0) width = settings.SidebarWidth;
            bool compact = width < SidebarCompactThreshold;
            if (!force && sidebarLayoutInitialized && compact == sidebarCompact) return;
            sidebarLayoutInitialized = true;
            sidebarCompact = compact;

            T<StackPanel>("SidebarBrand").Margin = compact ? new Thickness(10, 18, 10, 16) : new Thickness(22, 24, 14, 24);
            T<Border>("SidebarBrandMark").HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            T<TextBlock>("SidebarBrandName").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            T<TextBlock>("SidebarBrandCaption").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            T<TextBlock>("NavGroupLocation").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            T<Border>("SidebarGroupSeparator").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            T<TextBlock>("NavGroupSystem").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            T<StackPanel>("SidebarFooter").Margin = compact ? new Thickness(8, 10, 8, 16) : new Thickness(16, 10, 16, 20);
            var statusCard = T<Border>("SidebarStatusCard");
            statusCard.Padding = compact ? new Thickness(0) : new Thickness(12, 10, 12, 10);
            statusCard.Width = compact ? 44 : double.NaN;
            statusCard.Height = compact ? 44 : double.NaN;
            statusCard.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            T<Grid>("SidebarStatusGrid").ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            T<System.Windows.Shapes.Ellipse>("SidebarDot").Margin = compact ? new Thickness(0) : new Thickness(0, 1, 10, 0);
            T<System.Windows.Shapes.Ellipse>("SidebarDot").VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
            T<TextBlock>("SidebarStatus").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            T<TextBlock>("SidebarVersion").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            T<Grid>("MainContentHost").Margin = compact ? new Thickness(24, 22, 24, 20) : new Thickness(32, 22, 32, 20);

            for (int i = 0; i < SidebarNavNames.Length; i++)
            {
                var button = T<Button>(SidebarNavNames[i]);
                var icon = T<TextBlock>(SidebarIconNames[i]);
                var label = T<TextBlock>(SidebarLabelNames[i]);
                button.Padding = compact ? new Thickness(0, 9, 0, 9) : new Thickness(14, 10, 14, 10);
                button.Margin = compact ? new Thickness(6, 3, 6, 3) : new Thickness(8, 2, 8, 2);
                button.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
                icon.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 14, 0);
                label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                var content = button.Content as StackPanel;
                if (content != null) content.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                System.Windows.Automation.AutomationProperties.SetName(button, label.Text);
                button.ToolTip = compact ? CreateSidebarCapsule(label.Text, label.FontSize) : null;
                ToolTipService.SetIsEnabled(button, compact);
                ToolTipService.SetInitialShowDelay(button, 160);
                ToolTipService.SetShowDuration(button, 30000);
                ToolTipService.SetPlacement(button, PlacementMode.Right);
            }
            UpdateSidebarStatusTooltip();
        }
        ToolTip CreateSidebarCapsule(string text, double fontSize)
        {
            var caption = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var surface = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(14, 9, 14, 9), Child = caption, SnapsToDevicePixels = true };
            surface.SetResourceReference(Border.BackgroundProperty, "Elevated");
            surface.SetResourceReference(Border.BorderBrushProperty, "CardEdge");
            return new ToolTip { Content = surface, Placement = PlacementMode.Right, HorizontalOffset = 10, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HasDropShadow = true };
        }
        void UpdateSidebarStatusTooltip()
        {
            var card = T<Border>("SidebarStatusCard");
            card.ToolTip = sidebarCompact ? CreateSidebarCapsule(T<TextBlock>("SidebarStatus").Text, T<TextBlock>("SidebarStatus").FontSize) : null;
            ToolTipService.SetIsEnabled(card, sidebarCompact);
            ToolTipService.SetInitialShowDelay(card, 160);
            ToolTipService.SetShowDuration(card, 30000);
            ToolTipService.SetPlacement(card, PlacementMode.Right);
        }
        void OnSidebarNavGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!sidebarCompact) return;
            var button = sender as Button;
            var tooltip = button?.ToolTip as ToolTip;
            if (tooltip == null) return;
            tooltip.PlacementTarget = button;
            tooltip.IsOpen = true;
        }
        void OnSidebarNavLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var button = sender as Button;
            var tooltip = button?.ToolTip as ToolTip;
            if (tooltip != null && !button.IsMouseOver) tooltip.IsOpen = false;
        }
        void OnSidebarDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (e.Canceled) return;
            Safe(() => CommitSidebarWidth(T<Grid>("RootSurface").ColumnDefinitions[0].ActualWidth));
        }
        void OnSidebarSplitterKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Home && e.Key != Key.End) return;
            e.Handled = true;
            Safe(() => AdjustSidebarWidthFromKey(e.Key));
        }
        void AdjustSidebarWidthFromKey(Key key)
        {
            double width = CurrentSidebarWidth();
            if (key == Key.Home) width = AppSettings.MinSidebarWidth;
            else if (key == Key.End) width = AppSettings.MaxSidebarWidth;
            else if (key == Key.Left) width = width < SidebarCompactThreshold ? AppSettings.MinSidebarWidth : width - 16;
            else if (key == Key.Right) width = width < SidebarCompactThreshold ? SidebarCompactThreshold + 16 : width + 16;
            CommitSidebarWidth(width);
        }
        void CommitSidebarWidth(double width)
        {
            if (width < SidebarCompactThreshold) width = AppSettings.MinSidebarWidth;
            width = AppSettings.NormalizeSidebarWidth(width);
            T<Grid>("RootSurface").ColumnDefinitions[0].Width = new GridLength(width);
            settings.SidebarWidth = width;
            store.Save(settings);
            T<Grid>("RootSurface").UpdateLayout();
            ApplySidebarLayout(width, true);
        }
        double CurrentSidebarWidth()
        {
            double width = T<Grid>("RootSurface").ColumnDefinitions[0].ActualWidth;
            return width > 0 ? width : settings.SidebarWidth;
        }
        public async Task InitializeAsync()
        {
            await ExecuteAsync(L.T("读取驱动状态"), RefreshAsync, false);
            if (settings.VerifyOnStartup && state != null && state.Healthy && state.Enabled && state.LocationAllowed) await ExecuteAsync(L.T("启动核验"), VerifyAsync, false);
            if (state != null && !state.Installed) { ShowPage("setup"); await ExecuteAsync(L.T("检测安装条件"), RefreshSetupAsync); }
            initialized = true;
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
            T<TextBlock>("SetupMemoryIntegrityText").Text = preflight.MemoryIntegrity == null ? L.T("未知") : L.T(preflight.MemoryIntegrity.Value ? "已开启" : "已关闭");
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
            if (store.IsReadOnlyDueToNewerSchema) throw new InvalidOperationException(L.T("检测到较新版本的配置，当前版本将以只读方式打开以保护原配置。"));
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
            return LocationVerification.IsWithinConfiguredTolerance(state.Configured, probe);
        }
        void UpdateStatus()
        {
            if (state == null)
            {
                T<TextBlock>("HeroStatus").Text = L.T("读取驱动状态中");
                T<TextBlock>("HeroCoordinates").Text = "—";
                T<TextBlock>("HeroDetail").Text = L.T("尚未读取 Windows 实际位置");
                T<TextBlock>("SidebarStatus").Text = L.T("正在读取状态");
                T<System.Windows.Shapes.Ellipse>("SidebarDot").Fill = Appearance.Brush(Window, "MutedText");
                T<TextBlock>("DriverStatusText").Text = "—";
                T<TextBlock>("PermissionStatusText").Text = "—";
                T<TextBlock>("SigningStatusText").Text = "—";
                T<Button>("DisableButton").IsEnabled = false;
                T<TextBlock>("ActualSource").Text = L.T("尚未核验");
                T<TextBlock>("ActualSource").Foreground = Appearance.Brush(Window, "Accent");
                T<TextBlock>("ActualCoordinates").Text = L.T("点击核验，读取 Windows 实际位置");
                T<TextBlock>("ActualAccuracy").Text = L.T("报告精度  —");
                T<TextBlock>("ActualDistance").Text = L.T("距驱动配置  —");
                T<TextBlock>("ActualTime").Text = L.T("尚无当前配置的采样记录");
                UpdateSidebarStatusTooltip();
                return;
            }
            bool fresh = probe != null && (DateTime.Now - probe.CapturedAt).TotalSeconds < 60;
            bool verified = fresh && Verified();
            string status = !state.Installed ? L.T("未找到虚拟 GNSS 驱动") : !state.Healthy ? L.T("驱动需要检查") : !state.Enabled ? L.T("虚拟定位已暂停") : !state.LocationAllowed ? L.T("Windows 定位权限已关闭") : verified ? L.T("系统虚拟定位已核验") : L.T("虚拟位置已配置 · 待核验");
            T<TextBlock>("HeroStatus").Text = status;
            T<TextBlock>("HeroCoordinates").Text = state.Configured?.Coordinates ?? L.T("驱动没有有效坐标");
            T<TextBlock>("HeroDetail").Text = verified ? L.T("Windows 实际读数与驱动坐标匹配") : L.T("以实际核验为准，不将配置开关当作生效证明");
            T<TextBlock>("SidebarStatus").Text = verified ? L.T("虚拟定位已核验") : state.Healthy && state.Enabled ? L.T("虚拟位置已配置") : state.Installed ? L.T("虚拟定位已暂停") : L.T("驱动未就绪");
            T<System.Windows.Shapes.Ellipse>("SidebarDot").Fill = Brush(verified ? "#91D9B0" : "#CAB376");
            UpdateSidebarStatusTooltip();
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
                var nav = T<Button>(navs[i]);
                nav.Background = active ? Appearance.Brush(Window, "NavActive") : Brushes.Transparent;
                nav.ApplyTemplate();
                var indicator = nav.Template.FindName("NavIndicator", nav) as Border;
                if (indicator != null) indicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
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
            bool hasFavorites = settings.Favorites.Count > 0;
            T<TextBlock>("FavoritesEmptyHint").Visibility = hasFavorites ? Visibility.Collapsed : Visibility.Visible;
            UpdateFavoriteActions();
        }
        void UpdateFavoriteActions()
        {
            bool canUseFavorite = !busy && T<ListBox>("FavoritesList").SelectedItem is LocationProfile;
            T<Button>("LoadFavoriteButton").IsEnabled = canUseFavorite;
            T<Button>("ApplyFavoriteButton").IsEnabled = canUseFavorite;
            T<Button>("DeleteFavoriteButton").IsEnabled = canUseFavorite;
        }
        void LoadSettingsControls()
        {
            loading = true;
            SelectTag(T<ComboBox>("LanguageSelector"), settings.Language, "auto");
            T<CheckBox>("SystemThemeToggle").IsChecked = settings.ThemeMode == "system";
            T<CheckBox>("NightModeToggle").IsChecked = settings.ThemeMode == "dark" || settings.ThemeMode == "system" && !Appearance.IsSystemLight();
            T<CheckBox>("NightModeToggle").IsEnabled = settings.ThemeMode != "system";
            SelectTag(T<ComboBox>("AccentSelector"), settings.AccentColor, "mint");
            T<Slider>("FontScaleSlider").Value = settings.FontScale;
            T<CheckBox>("StartupToggle").IsChecked = StartupRegistration.Enabled;
            T<CheckBox>("CloseToTrayToggle").IsChecked = settings.CloseToTray;
            T<CheckBox>("VerifyStartupToggle").IsChecked = settings.VerifyOnStartup;
            T<CheckBox>("VerifyApplyToggle").IsChecked = settings.VerifyAfterApply;
            T<ComboBox>("SearchProvider").SelectedIndex = settings.Geocoder == "Google" ? 1 : 0;
            T<ComboBox>("SettingsProvider").SelectedIndex = settings.Geocoder == "Google" ? 1 : 0;
            loading = false; UpdateFontScaleLabel(); UpdateProviderLabels(); UpdateKeyStatus();
        }
        static void SelectTag(ComboBox combo, string tag, string fallback)
        {
            string wanted = string.IsNullOrEmpty(tag) ? fallback : tag;
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => Convert.ToString(item.Tag) == wanted);
            if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0;
        }
        void UpdateThemeChoice()
        {
            if (loading) return;
            settings.ThemeMode = T<CheckBox>("SystemThemeToggle").IsChecked == true ? "system" : T<CheckBox>("NightModeToggle").IsChecked == true ? "dark" : "light";
            T<CheckBox>("NightModeToggle").IsEnabled = settings.ThemeMode != "system";
            Safe(SaveAppearanceSettings);
        }
        void SaveAppearanceSettings()
        {
            if (loading) return;
            var accent = T<ComboBox>("AccentSelector").SelectedItem as ComboBoxItem;
            if (accent != null) settings.AccentColor = Convert.ToString(accent.Tag);
            settings.FontScale = SnapFontScale(T<Slider>("FontScaleSlider").Value);
            store.Save(settings);
            ApplyAppearance();
        }
        static int SnapFontScale(double value) => Math.Max(90, Math.Min(125, (int)(Math.Round(value / 5.0) * 5)));
        void ApplyAppearance()
        {
            Appearance.Apply(Window, settings);
            ApplyFontScale();
            SetTitleBarTheme();
            ApplySidebarLayout(CurrentSidebarWidth(), true);
            ShowPage(currentPage);
            UpdateStatus();
            ReapplyNotice();
            RefreshNoticeCertificate();
        }
        void ApplyFontScale()
        {
            double scale = Math.Max(90, Math.Min(125, settings.FontScale)) / 100.0;
            foreach (var pair in baseFontSizes)
            {
                double size = Math.Round(pair.Value * scale, 1);
                var control = pair.Key as Control;
                if (control != null) control.FontSize = size;
                else
                {
                    var text = pair.Key as TextBlock;
                    if (text != null) text.FontSize = size;
                }
            }
            UpdateFontScaleLabel();
        }
        void UpdateFontScaleLabel()
        {
            var value = Window.FindName("FontScaleValue") as TextBlock;
            if (value != null) value.Text = Math.Max(90, Math.Min(125, settings.FontScale)) + "%";
        }
        void ApplyLocale()
        {
            ApplyStaticTranslations();
            LoadSettingsControls();
            T<Button>("ApplyButton").Content = L.T(settings.VerifyAfterApply ? "应用并核验" : "应用位置");
            ApplySidebarLayout(CurrentSidebarWidth(), true);
            UpdateStatus(); UpdateTrayLanguage(); UpdateSetupStatus(); ShowPage(currentPage);
            ReapplyNotice();
            RefreshNoticeCertificate();
        }
        void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (disposed || settings == null) return;
            try { Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed) return;
                if (settings.Language == "auto") { L.Set("auto"); ApplyLocale(); }
                if (settings.ThemeMode == "system" || e.Category == UserPreferenceCategory.Accessibility) ApplyAppearance();
            })); } catch (InvalidOperationException) { }
        }
        void SetTitleBarTheme()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(Window).Handle;
                if (handle == IntPtr.Zero) return;
                int dark = !SystemParameters.HighContrast && (settings.ThemeMode == "dark" || settings.ThemeMode == "system" && !Appearance.IsSystemLight()) ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref dark, 4);
                int backdrop = SystemParameters.HighContrast ? 1 : 2; // DWMSBT_NONE in high contrast; DWMSBT_MAINWINDOW otherwise.
                systemBackdropApplied = !SystemParameters.HighContrast && DwmSetWindowAttribute(handle, 38, ref backdrop, 4) >= 0;
                if (SystemParameters.HighContrast) DwmSetWindowAttribute(handle, 38, ref backdrop, 4);
                Brush surface = systemBackdropApplied ? Brushes.Transparent : Appearance.Brush(Window, "AppBg");
                Window.Background = surface;
                T<Grid>("RootSurface").Background = surface;
            }
            catch
            {
                systemBackdropApplied = false;
                Brush fallback = Appearance.Brush(Window, "AppBg");
                Window.Background = fallback;
                if (Window.FindName("RootSurface") is Grid root) root.Background = fallback;
            }
        }
        void UpdateProviderLabels()
        {
            geocoder.SetLanguage(L.Language);
            bool google = settings.Geocoder == "Google";
            T<TextBlock>("SearchPrivacyText").Text = google ? L.T("地址将发送至 Google Geocoding。需要你自己的 API Key；只在点击搜索时查询。") : L.T("地址将发送至 OpenStreetMap / Nominatim。仅在点击搜索时查询；坐标可离线解析。");
            T<TextBlock>("SearchAttribution").Text = google ? L.T("地址结果由 Google 提供 · Google Maps 链接无需 API Key") : "© OpenStreetMap contributors · Nominatim";
        }
        void UpdateKeyStatus() => T<TextBlock>("GoogleKeyStatus").Text = string.IsNullOrEmpty(SettingsStore.Unprotect(settings.GoogleKeyProtected)) ? L.T("未配置 · Key 将按当前 Windows 用户加密保存") : L.T("已配置 · Key 已加密保存（当前 Windows 用户）");
        void SaveSettings()
        {
            bool desiredStartup = T<CheckBox>("StartupToggle").IsChecked == true;
            string startupError = null;
            settings.CloseToTray = T<CheckBox>("CloseToTrayToggle").IsChecked == true;
            settings.VerifyOnStartup = T<CheckBox>("VerifyStartupToggle").IsChecked == true;
            settings.VerifyAfterApply = T<CheckBox>("VerifyApplyToggle").IsChecked == true;
            settings.Geocoder = T<ComboBox>("SettingsProvider").SelectedIndex == 1 ? "Google" : "OpenStreetMap";
            string key = T<System.Windows.Controls.PasswordBox>("GoogleKeyInput").Password.Trim();
            if (!string.IsNullOrEmpty(key)) settings.GoogleKeyProtected = SettingsStore.Protect(key);
            T<System.Windows.Controls.PasswordBox>("GoogleKeyInput").Clear();
            store.Save(settings);
            try { StartupRegistration.Set(desiredStartup); }
            catch (Exception ex) { startupError = L.T("登录自启动设置未完成；其他设置仍已保存。"); store.Log(startupError + " " + ex.Message); }
            LoadSettingsControls();
            T<Button>("ApplyButton").Content = settings.VerifyAfterApply ? L.T("应用并核验") : L.T("应用位置");
            store.Log(L.T("设置已保存；登录自启动") + (desiredStartup ? L.T("开启") : L.T("关闭")) + "。");
            if (startupError == null) Notice(L.T("设置已保存。")); else Notice(startupError, true);
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
            catch (Exception ex)
            {
                string message = ex.Message;
                string secret = SettingsStore.Unprotect(settings.GoogleKeyProtected);
                if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, L.T("[已隐藏 Key]"));
                store.Log(action + L.T("未完成：") + message);
                if (IsAccessDenied(ex)) NoticeFromSource(AccessDeniedNoticeSource, true); else Notice(message, true);
            }
            finally { busy = false; SetBusy(false); UpdateStatus(); }
        }
        static bool IsAccessDenied(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is UnauthorizedAccessException || current is System.Security.SecurityException) return true;
                var management = current as System.Management.ManagementException;
                if (management != null && management.ErrorCode == System.Management.ManagementStatus.AccessDenied) return true;
                string message = current.Message?.Trim();
                if (string.Equals(message, "拒绝访问。", StringComparison.OrdinalIgnoreCase) || string.Equals(message, "Access is denied.", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        void SetBusy(bool value)
        {
            foreach (string name in new[] { "ApplyButton", "VerifyButton", "RefreshButton", "SearchButton", "DisableButton", "PrepareSystemButton", "InstallDriverButton", "RemoveDriverButton" }) T<Button>(name).IsEnabled = !value;
            UpdateFavoriteActions();
            UpdateSetupStatus();
        }
        void Safe(Action work) { try { work(); } catch (Exception ex) { store.Log(L.T("操作未完成：") + ex.Message); Notice(ex.Message, true); } }
        void Notice(string text, bool warning = false)
        {
            lastNoticeSource = L.SourceKeyFor(text);
            DisplayNotice(lastNoticeSource == null ? text : L.T(lastNoticeSource), warning);
        }
        void NoticeFromSource(string source, bool warning = false)
        {
            lastNoticeSource = source;
            DisplayNotice(L.T(source), warning);
        }
        void ReapplyNotice()
        {
            if (lastNoticeSource != null) NoticeFromSource(lastNoticeSource, lastNoticeWarning);
            else if (lastNoticeText != null) Notice(lastNoticeText, lastNoticeWarning);
        }
        void DisplayNotice(string text, bool warning)
        {
            lastNoticeText = text; lastNoticeWarning = warning;
            noticeAwardMode = warning && string.Equals(lastNoticeSource, AccessDeniedNoticeSource, StringComparison.Ordinal);
            var bar = T<Border>("NoticeBar");
            var headline = T<TextBlock>("NoticeHeadline");
            StopNoticeAnimations(bar, headline);
            bar.Visibility = Visibility.Visible;
            bar.Focusable = noticeAwardMode;
            KeyboardNavigation.SetIsTabStop(bar, noticeAwardMode);
            bar.Cursor = noticeAwardMode ? Cursors.Hand : Cursors.Arrow;
            bar.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, noticeAwardMode ? L.T("拒绝访问") : text);
            if (noticeAwardMode) bar.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, L.T("双击查看完整喜报"));
            else bar.ClearValue(System.Windows.Automation.AutomationProperties.HelpTextProperty);
            bar.ToolTip = noticeAwardMode ? L.T("双击查看完整喜报") : null;
            T<TextBlock>("NoticeText").Text = text;
            T<StackPanel>("NoticeStandardPanel").Visibility = noticeAwardMode ? Visibility.Collapsed : Visibility.Visible;
            T<Grid>("NoticeAwardPanel").Visibility = noticeAwardMode ? Visibility.Visible : Visibility.Collapsed;
            if (!noticeAwardMode)
            {
                bar.Background = Appearance.Brush(Window, warning ? "DangerSoft" : "AccentSoft");
                bar.BorderBrush = Appearance.Brush(Window, "CardEdge");
                bar.BorderThickness = new Thickness(1);
                bar.Effect = null;
                T<TextBlock>("NoticeText").Foreground = Appearance.Brush(Window, warning ? "Danger" : "SoftText");
                activeNoticeCertificate?.Close();
                return;
            }

            T<TextBlock>("NoticeAwardLabel").Text = L.T("喜报");
            T<TextBlock>("NoticeOpenHint").Text = L.T("双击查看完整喜报");
            T<TextBlock>("NoticeAwardTeaser").Text = L.T("Windows 拒绝了本次操作…");
            headline.Text = L.T("拒绝访问");
            headline.TextAlignment = TextAlignment.Center;
            headline.HorizontalAlignment = HorizontalAlignment.Stretch;
            var awardPanel = T<Grid>("NoticeAwardPanel");
            bar.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, headline.Text);
            if (SystemParameters.HighContrast)
            {
                bar.Background = SystemColors.WindowBrush;
                bar.BorderBrush = SystemColors.WindowTextBrush;
                awardPanel.Background = SystemColors.WindowBrush;
                headline.Foreground = SystemColors.WindowTextBrush;
                headline.Effect = null;
                T<TextBlock>("NoticeOpenHint").Foreground = SystemColors.WindowTextBrush;
                T<TextBlock>("NoticeAwardTeaser").Foreground = SystemColors.WindowTextBrush;
            }
            else
            {
                bar.Background = CreateNoticeCertificateBackgroundBrush();
                awardPanel.Background = Brushes.Transparent;
                T<TextBlock>("NoticeOpenHint").Foreground = new SolidColorBrush(Color.FromRgb(255, 228, 157));
                T<TextBlock>("NoticeAwardTeaser").Foreground = new SolidColorBrush(Color.FromRgb(105, 8, 18));
                var frameBrush = new SolidColorBrush(NoticeSpectrum[0]);
                bar.BorderBrush = frameBrush;
                headline.Foreground = CreateGoldBrush(out var highlight);
                headline.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(73, 0, 8), BlurRadius = 4, ShadowDepth = 2, Opacity = 0.95 };
                if (NoticeAnimationsAllowed())
                {
                    StartSpectrumAnimation(frameBrush);
                    StartGoldAnimation(highlight);
                }
            }
            bar.BorderThickness = new Thickness(2);
            bar.Effect = SystemParameters.HighContrast ? null : new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(207, 166, 70), BlurRadius = 13, ShadowDepth = 0, Opacity = 0.22 };
            RefreshNoticeCertificate();
        }
        static bool NoticeAnimationsAllowed() => !SystemParameters.HighContrast && SystemParameters.ClientAreaAnimation;
        static LinearGradientBrush CreateGoldBrush(out GradientStop highlight)
        {
            highlight = new GradientStop(Color.FromRgb(255, 240, 174), 0.5);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(193, 122, 10), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(240, 199, 71), 0.24));
            brush.GradientStops.Add(highlight);
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(231, 176, 43), 0.72));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(193, 122, 10), 1));
            return brush;
        }
        static void StartSpectrumAnimation(SolidColorBrush brush)
        {
            var animation = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(7.2), RepeatBehavior = RepeatBehavior.Forever };
            for (int i = 1; i <= NoticeSpectrum.Length; i++) animation.KeyFrames.Add(new LinearColorKeyFrame(NoticeSpectrum[i % NoticeSpectrum.Length], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i * 1.2))));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
        static void StartGoldAnimation(GradientStop highlight)
        {
            highlight.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation
            {
                From = Color.FromRgb(255, 237, 161), To = Color.FromRgb(255, 255, 232), Duration = TimeSpan.FromSeconds(2.4),
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        static void StopNoticeAnimations(Border bar, TextBlock headline)
        {
            var frameBrush = bar.BorderBrush as SolidColorBrush;
            if (frameBrush != null && !frameBrush.IsFrozen) frameBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            var gradient = headline.Foreground as LinearGradientBrush;
            if (gradient != null && !gradient.IsFrozen && gradient.GradientStops.Count > 2 && !gradient.GradientStops[2].IsFrozen) gradient.GradientStops[2].BeginAnimation(GradientStop.ColorProperty, null);
        }
        void HandleNoticeBarClickCount(int clickCount, bool showModal)
        {
            if (!noticeAwardMode) return;
            if (clickCount >= 2)
            {
                OpenNoticeCertificate(showModal);
            }
            else T<Border>("NoticeBar").Focus();
        }
        void OnNoticeBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!noticeAwardMode) return;
            HandleNoticeBarClickCount(e.ClickCount, true);
            if (e.ClickCount >= 2) e.Handled = true;
        }
        void OnNoticeBarKeyDown(object sender, KeyEventArgs e)
        {
            if (HandleNoticeBarKey(e.Key, true)) e.Handled = true;
        }
        bool HandleNoticeBarKey(Key key, bool showModal)
        {
            if (!noticeAwardMode || key != Key.Enter && key != Key.Space) return false;
            OpenNoticeCertificate(showModal);
            return true;
        }
        void OpenNoticeCertificate(bool showModal)
        {
            if (!noticeAwardMode || string.IsNullOrWhiteSpace(lastNoticeText)) return;
            if (activeNoticeCertificate != null)
            {
                if (activeNoticeCertificate.WindowState == WindowState.Minimized) activeNoticeCertificate.WindowState = WindowState.Normal;
                activeNoticeCertificate.Activate();
                return;
            }
            var dialog = CreateNoticeCertificateWindow();
            activeNoticeCertificate = dialog;
            RefreshNoticeCertificate();
            dialog.Closed += (s, e) =>
            {
                StopNoticeMusic();
                if (ReferenceEquals(activeNoticeCertificate, dialog))
                {
                    activeNoticeCertificate = null;
                    activeNoticeCertificateSurface = null;
                    activeNoticeCertificateFrame = null;
                    activeNoticeCertificateInner = null;
                    activeNoticeCertificateHeadlinePanel = null;
                    activeNoticeCertificateHeadline = null;
                    activeNoticeCertificateDetailsPanel = null;
                    activeNoticeCertificateDetails = null;
                    activeNoticeCertificateCloseButton = null;
                }
            };
            StartNoticeMusic();
            if (showModal) dialog.ShowDialog(); else dialog.Show();
        }
        void StartNoticeMusic()
        {
            if (noticeMusicResources == null)
                noticeMusicResources = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(name => name.StartsWith("SarabDesktop.NoticeMusic.", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (noticeMusicResources.Length != 20)
            {
                store.Log("Certificate music is unavailable: expected 20 embedded 8-bit tracks but found " + noticeMusicResources.Length + ".");
                return;
            }
            noticeMusicRequested = true;
            PlayRandomNoticeTrack();
        }
        MediaPlayer CreateNoticeMusicPlayer()
        {
            var player = new MediaPlayer { Volume = CertificateMusicVolume(preview), IsMuted = false };
            player.MediaOpened += (s, e) =>
            {
                var media = s as MediaPlayer;
                if (!noticeMusicRequested || !ReferenceEquals(media, noticeMusicPlayer)) return;
                media.Play();
                if (preview) noticeMusicSmokeOpened?.TrySetResult(true);
            };
            player.MediaEnded += (s, e) => OnNoticeMusicTrackEnded(s as MediaPlayer);
            player.MediaFailed += (s, e) =>
            {
                var media = s as MediaPlayer;
                if (!ReferenceEquals(media, noticeMusicPlayer)) return;
                noticeMusicRequested = false;
                store.Log("Certificate music playback failed: " + (e.ErrorException?.Message ?? "unknown media error"));
                try { media.Close(); } catch { }
                noticeMusicPlayer = null;
                DeleteActiveNoticeMusicFile();
                if (preview) noticeMusicSmokeOpened?.TrySetResult(false);
            };
            return player;
        }
        void OnNoticeMusicTrackEnded(MediaPlayer endedPlayer)
        {
            try
            {
                Window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (noticeMusicRequested && activeNoticeCertificate != null && ReferenceEquals(endedPlayer, noticeMusicPlayer)) PlayRandomNoticeTrack();
                }));
            }
            catch (InvalidOperationException) { }
        }
        void PlayRandomNoticeTrack()
        {
            if (!noticeMusicRequested || noticeMusicResources == null || noticeMusicResources.Length == 0) return;
            int index = ChooseNextNoticeMusicTrackIndex(noticeMusicResources.Length, previousNoticeMusicIndex, noticeMusicRandom);
            previousNoticeMusicIndex = index;
            OpenNoticeMusicTrack(index);
        }
        void OpenNoticeMusicTrack(int index)
        {
            if (!noticeMusicRequested || noticeMusicResources == null || index < 0 || index >= noticeMusicResources.Length) return;
            var oldPlayer = noticeMusicPlayer;
            noticeMusicPlayer = null;
            if (oldPlayer != null) { try { oldPlayer.Stop(); oldPlayer.Close(); } catch { } }
            DeleteActiveNoticeMusicFile();
            CleanupNoticeMusicFiles();
            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "SarabDesktop", "NoticeMusic-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                activeNoticeMusicPath = Path.Combine(folder, "track-" + (index + 1).ToString("D2", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".wav");
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(noticeMusicResources[index]))
                {
                    if (resource == null) throw new FileNotFoundException("Embedded certificate music resource is missing.", noticeMusicResources[index]);
                    using (var output = new FileStream(activeNoticeMusicPath, FileMode.Create, FileAccess.Write, FileShare.Read)) resource.CopyTo(output);
                }
                noticeMusicTempFiles.Add(activeNoticeMusicPath);
                noticeMusicPlayer = CreateNoticeMusicPlayer();
                noticeMusicPlayer.Open(new Uri(activeNoticeMusicPath, UriKind.Absolute));
            }
            catch (Exception ex)
            {
                noticeMusicRequested = false;
                store.Log("Certificate music could not be opened: " + ex.Message);
                if (noticeMusicPlayer != null) { try { noticeMusicPlayer.Close(); } catch { } noticeMusicPlayer = null; }
                DeleteActiveNoticeMusicFile();
            }
        }
        void DeleteActiveNoticeMusicFile()
        {
            if (string.IsNullOrEmpty(activeNoticeMusicPath)) return;
            string path = activeNoticeMusicPath;
            activeNoticeMusicPath = null;
            if (!TryDeleteNoticeMusicFile(path)) noticeMusicTempFiles.Add(path);
            else noticeMusicTempFiles.Remove(path);
        }
        void CleanupNoticeMusicFiles()
        {
            foreach (string path in noticeMusicTempFiles.ToArray())
            {
                if (TryDeleteNoticeMusicFile(path)) noticeMusicTempFiles.Remove(path);
            }
        }
        static bool TryDeleteNoticeMusicFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); return !File.Exists(path); }
            catch { return false; }
        }
        static async Task RetryDeleteNoticeMusicFiles(string[] paths)
        {
            foreach (int delay in new[] { 250, 750, 1500, 3000, 5000 })
            {
                await Task.Delay(delay).ConfigureAwait(false);
                foreach (string path in paths) TryDeleteNoticeMusicFile(path);
            }
        }
        internal static double CertificateMusicVolume(bool previewMode) => previewMode ? 0.0 : 1.0;
        internal static int ChooseNextNoticeMusicTrackIndex(int trackCount, int previousIndex, Random random)
        {
            if (trackCount < 1) throw new ArgumentOutOfRangeException(nameof(trackCount));
            if (random == null) throw new ArgumentNullException(nameof(random));
            bool avoidPrevious = trackCount > 1 && previousIndex >= 0 && previousIndex < trackCount;
            int index = random.Next(avoidPrevious ? trackCount - 1 : trackCount);
            return avoidPrevious && index >= previousIndex ? index + 1 : index;
        }
        void StopNoticeMusic()
        {
            noticeMusicRequested = false;
            if (noticeMusicPlayer != null)
            {
                var player = noticeMusicPlayer;
                noticeMusicPlayer = null;
                try { player.Stop(); player.Close(); } catch { }
            }
            DeleteActiveNoticeMusicFile();
            CleanupNoticeMusicFiles();
            if (noticeMusicTempFiles.Count > 0) _ = RetryDeleteNoticeMusicFiles(noticeMusicTempFiles.ToArray());
            previousNoticeMusicIndex = -1;
        }
        Window CreateNoticeCertificateWindow()
        {
            var dialog = new Window
            {
                Title = L.T("完整喜报"), Owner = Window, Width = 620, Height = 460, MinWidth = 520, MinHeight = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false, FontFamily = Window.FontFamily,
                FontSize = Window.FontSize, Resources = Window.Resources
            };
            dialog.SetResourceReference(Window.BackgroundProperty, "AppBg");
            dialog.SetResourceReference(Window.ForegroundProperty, "Text");
            dialog.PreviewKeyDown += (s, e) => { if (HandleNoticeCertificateKey(dialog, e.Key)) e.Handled = true; };

            var frame = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(18), Padding = new Thickness(7) };
            frame.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(255, 215, 112));
            frame.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(142, 14, 22));
            var inner = new Border { BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromArgb(220, 255, 218, 126)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(34, 26, 34, 22) };
            inner.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : CreateNoticeCertificateBackgroundBrush();
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var headline = new TextBlock { Text = L.T("拒绝访问"), FontSize = 34, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 0) };
            if (SystemParameters.HighContrast) headline.Foreground = SystemColors.WindowTextBrush;
            else
            {
                headline.Foreground = CreateGoldBrush(out var highlight);
                if (NoticeAnimationsAllowed()) StartGoldAnimation(highlight);
                headline.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(73, 0, 8), BlurRadius = 4, ShadowDepth = 2, Opacity = 0.95 };
            }
            var headlinePanel = new Border
            {
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brushes.Transparent,
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.Transparent,
                BorderThickness = SystemParameters.HighContrast ? new Thickness(1) : new Thickness(0), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 5, 14, 7),
                Margin = new Thickness(18, 0, 18, 14), HorizontalAlignment = HorizontalAlignment.Stretch, Child = headline
            };
            Grid.SetRow(headlinePanel, 1); layout.Children.Add(headlinePanel);
            var details = new TextBlock { Text = lastNoticeText, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 15, LineHeight = 25, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            details.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(255, 248, 220));
            var detailsPanel = new Border
            {
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromArgb(224, 105, 8, 18)),
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromArgb(220, 255, 220, 129)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(15, 11, 15, 11),
                Margin = new Thickness(4, 0, 4, 16), VerticalAlignment = VerticalAlignment.Center, Child = details
            };
            Grid.SetRow(detailsPanel, 2); layout.Children.Add(detailsPanel);
            var close = new Button { Content = L.T("关闭此窗口"), MinWidth = 142, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(18, 9, 18, 9), FontWeight = FontWeights.SemiBold };
            if (!SystemParameters.HighContrast)
            {
                close.Background = new SolidColorBrush(Color.FromRgb(255, 219, 125));
                close.Foreground = new SolidColorBrush(Color.FromRgb(106, 20, 23));
                close.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 239, 183));
            }
            close.Click += (s, e) => dialog.Close();
            Grid.SetRow(close, 3); layout.Children.Add(close);
            var surface = new Grid { Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Appearance.Brush(Window, "AppBg") };
            surface.Children.Add(frame);
            activeNoticeCertificateSurface = surface;
            activeNoticeCertificateFrame = frame;
            activeNoticeCertificateInner = inner;
            activeNoticeCertificateHeadlinePanel = headlinePanel;
            activeNoticeCertificateHeadline = headline;
            activeNoticeCertificateDetailsPanel = detailsPanel;
            activeNoticeCertificateDetails = details;
            activeNoticeCertificateCloseButton = close;
            inner.Child = layout; frame.Child = inner; dialog.Content = surface;
            RefreshNoticeCertificateAppearance(SystemParameters.HighContrast);
            return dialog;
        }
        void RefreshNoticeCertificate()
        {
            if (activeNoticeCertificate == null) return;
            activeNoticeCertificate.Title = L.T("完整喜报");
            if (activeNoticeCertificateHeadline != null) activeNoticeCertificateHeadline.Text = L.T("拒绝访问");
            if (activeNoticeCertificateDetails != null) activeNoticeCertificateDetails.Text = lastNoticeText;
            if (activeNoticeCertificateCloseButton != null) activeNoticeCertificateCloseButton.Content = L.T("关闭此窗口");
            RefreshNoticeCertificateAppearance(SystemParameters.HighContrast);
        }
        void RefreshNoticeCertificateAppearance(bool highContrast)
        {
            if (activeNoticeCertificate == null || activeNoticeCertificateSurface == null || activeNoticeCertificateFrame == null || activeNoticeCertificateInner == null || activeNoticeCertificateHeadlinePanel == null || activeNoticeCertificateHeadline == null || activeNoticeCertificateDetailsPanel == null) return;
            bool animate = !highContrast && SystemParameters.ClientAreaAnimation;
            var frame = activeNoticeCertificateFrame;
            var inner = activeNoticeCertificateInner;
            var titlePanel = activeNoticeCertificateHeadlinePanel;
            var title = activeNoticeCertificateHeadline;
            var detailsPanel = activeNoticeCertificateDetailsPanel;
            var close = activeNoticeCertificateCloseButton;

            activeNoticeCertificateSurface.Background = highContrast ? SystemColors.WindowBrush : Appearance.Brush(Window, "AppBg");
            frame.BorderBrush = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(255, 215, 112));
            frame.Background = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(142, 14, 22));
            inner.BorderBrush = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromArgb(220, 255, 218, 126));
            inner.Background = highContrast ? SystemColors.WindowBrush : CreateNoticeCertificateBackgroundBrush();
            titlePanel.Background = highContrast ? SystemColors.WindowBrush : Brushes.Transparent;
            titlePanel.BorderBrush = highContrast ? SystemColors.WindowTextBrush : Brushes.Transparent;
            titlePanel.BorderThickness = highContrast ? new Thickness(1) : new Thickness(0);
            title.Foreground = highContrast ? SystemColors.WindowTextBrush : CreateGoldBrush(out var highlight);
            title.Effect = highContrast ? null : new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(73, 0, 8), BlurRadius = 4, ShadowDepth = 2, Opacity = 0.95 };
            if (!highContrast && animate && title.Foreground is LinearGradientBrush gradient && gradient.GradientStops.Count > 2) StartGoldAnimation(gradient.GradientStops[2]);
            detailsPanel.Background = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromArgb(224, 105, 8, 18));
            detailsPanel.BorderBrush = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromArgb(220, 255, 220, 129));
            if (activeNoticeCertificateDetails != null) activeNoticeCertificateDetails.Foreground = highContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(255, 248, 220));
            if (close != null)
            {
                close.ClearValue(Control.BackgroundProperty);
                close.ClearValue(Control.ForegroundProperty);
                close.ClearValue(Control.BorderBrushProperty);
                if (!highContrast)
                {
                    close.Background = new SolidColorBrush(Color.FromRgb(255, 219, 125));
                    close.Foreground = new SolidColorBrush(Color.FromRgb(106, 20, 23));
                    close.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 239, 183));
                }
            }
        }
        static ImageBrush CreateNoticeCertificateBackgroundBrush()
        {
            const string resourceName = "SarabDesktop.NoticeCertificateBackground.jpg";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new FileNotFoundException("Embedded certificate background image is missing.", resourceName);
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
                brush.Freeze();
                return brush;
            }
        }
        static bool HandleNoticeCertificateKey(Window dialog, Key key)
        {
            if (key != Key.Escape) return false;
            dialog.Close();
            return true;
        }
        Brush Brush(string color)
        {
            string key = color == "#91D9B0" ? "Success" : color == "#CAB376" ? "Warning" : color == "#E5A3AD" ? "Danger" : color == "#C5B5F3" ? "Accent" : null;
            return key == null ? (Brush)new BrushConverter().ConvertFromString(color) : Appearance.Brush(Window, key);
        }
        void CaptureBaseFontSizes(DependencyObject element, HashSet<DependencyObject> seen)
        {
            if (!seen.Add(element)) return;
            double fontSize = 0;
            var control = element as Control;
            if (control != null) fontSize = control.FontSize;
            else
            {
                var text = element as TextBlock;
                if (text != null) fontSize = text.FontSize;
            }
            var framework = element as FrameworkElement;
            if (framework != null && fontSize > 0 && !baseFontSizes.ContainsKey(framework)) baseFontSizes.Add(framework, fontSize);
            foreach (var child in LogicalTreeHelper.GetChildren(element)) if (child is DependencyObject) CaptureBaseFontSizes((DependencyObject)child, seen);
        }
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
        public void ShowWindow()
        {
            bool restored = !Window.IsVisible || Window.WindowState == WindowState.Minimized;
            Window.Show(); if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal; Window.Activate();
            if (restored && initialized && !busy)
            {
                probe = null; UpdateStatus();
                Window.Dispatcher.BeginInvoke(new Action(async () => await ExecuteAsync(L.T("刷新状态"), RefreshAsync, false)));
            }
        }
        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!closing && !preview && settings.CloseToTray) { e.Cancel = true; Window.Hide(); }
            else if (!preview) { Dispose(); Application.Current.Shutdown(); }
        }
        public void Exit() { closing = true; Window.Close(); }
        public void Dispose() { if (disposed) return; disposed = true; StopNoticeMusic(); timer?.Stop(); if (systemEventsSubscribed) { SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged; systemEventsSubscribed = false; } if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } geocoder.Dispose(); }
        public async Task RenderAsync(string directory)
        {
            Directory.CreateDirectory(directory);
            string oldError = Path.Combine(directory, "render-error.txt");
            if (File.Exists(oldError)) File.Delete(oldError);
            Window.Left = -20000; Window.Top = -20000; Window.ShowInTaskbar = false; Window.Show();
            await InitializeAsync();
            await ExecuteAsync(L.T("检测安装条件"), RefreshSetupAsync, false);
            foreach (string page in new[] { "dashboard", "search", "favorites", "settings", "setup", "logs" })
            {
                ShowPage(page); T<Grid>("RootSurface").Background = Appearance.Brush(Window, "AppBg"); Window.UpdateLayout();
                await Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var root = T<FrameworkElement>("RootSurface");
                var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var output = File.Create(Path.Combine(directory, page + ".png"))) encoder.Save(output);
            }
            T<Grid>("RootSurface").ColumnDefinitions[0].Width = new GridLength(AppSettings.MinSidebarWidth);
            ApplySidebarLayout(AppSettings.MinSidebarWidth, true);
            ShowPage("dashboard"); T<Grid>("RootSurface").Background = Appearance.Brush(Window, "AppBg"); Window.UpdateLayout();
            await Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var compactRoot = T<FrameworkElement>("RootSurface");
            var compactImage = new RenderTargetBitmap((int)compactRoot.ActualWidth, (int)compactRoot.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            compactImage.Render(compactRoot);
            var compactEncoder = new PngBitmapEncoder(); compactEncoder.Frames.Add(BitmapFrame.Create(compactImage));
            using (var output = File.Create(Path.Combine(directory, "sidebar-compact.png"))) compactEncoder.Save(output);
            NoticeFromSource(AccessDeniedNoticeSource, true);
            Window.UpdateLayout();
            await Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            SaveVisual(T<FrameworkElement>("RootSurface"), Path.Combine(directory, "access-denied-notice.png"));
            OpenNoticeCertificate(false);
            await Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (activeNoticeCertificate != null) SaveVisual(activeNoticeCertificate, Path.Combine(directory, "access-denied-certificate.png"));
            activeNoticeCertificate?.Close();
            closing = true; Window.Close();
        }
        static void SaveVisual(FrameworkElement element, string path)
        {
            var windowElement = element as Window;
            if (windowElement != null && windowElement.Content is FrameworkElement content) element = content;
            var root = element as Grid;
            Window owner = windowElement ?? Window.GetWindow(element);
            Brush originalRootBackground = root?.Background;
            bool needsOpaqueCaptureSurface = root != null && owner != null && ReferenceEquals(originalRootBackground, Brushes.Transparent);
            try
            {
                if (needsOpaqueCaptureSurface) root.Background = Appearance.Brush(owner, "AppBg");
                element.UpdateLayout();
                int width = (int)Math.Ceiling(element.ActualWidth), height = (int)Math.Ceiling(element.ActualHeight);
                if (width < 1 || height < 1) throw new InvalidDataException("Cannot render a zero-sized UI element: " + element.GetType().Name);
                var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                image.Render(element);
                var opaqueSurface = new DrawingVisual();
                using (var drawing = opaqueSurface.RenderOpen())
                {
                    Brush backdrop = SystemParameters.HighContrast ? SystemColors.WindowBrush : owner == null ? Brushes.White : Appearance.Brush(owner, "AppBg");
                    drawing.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
                    drawing.DrawImage(image, new Rect(0, 0, width, height));
                }
                var opaqueImage = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                opaqueImage.Render(opaqueSurface);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(opaqueImage));
                using (var output = File.Create(path)) encoder.Save(output);
            }
            finally
            {
                if (needsOpaqueCaptureSurface) root.Background = originalRootBackground;
            }
        }
        static bool IsFullyOpaqueScreenshot(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(Path.GetFullPath(path)); bitmap.EndInit();
            var pixels = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            int stride = pixels.PixelWidth * 4;
            var buffer = new byte[stride * pixels.PixelHeight];
            pixels.CopyPixels(buffer, stride, 0);
            for (int i = 3; i < buffer.Length; i += 4) if (buffer[i] != 255) return false;
            return buffer.Length > 0;
        }
        static void WriteNoticeMusicSmokeTrack(string path)
        {
            const int sampleRate = 11025;
            int dataLength = sampleRate;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                Action<string> writeFourCc = value => writer.Write(value.Select(character => (byte)character).ToArray());
                writeFourCc("RIFF"); writer.Write(36 + dataLength); writeFourCc("WAVE");
                writeFourCc("fmt "); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(sampleRate); writer.Write(sampleRate); writer.Write((short)1); writer.Write((short)8);
                writeFourCc("data"); writer.Write(dataLength);
                for (int i = 0; i < dataLength; i++) writer.Write((byte)(128 + 24 * Math.Sin(2 * Math.PI * 440 * i / sampleRate)));
            }
        }
        public async Task UiSmokeAsync(string directory)
        {
            Directory.CreateDirectory(directory);
            Window.Left = -20000; Window.Top = -20000; Window.ShowInTaskbar = false; Window.Show();
            var results = new List<string>();
            await InitializeAsync();
            bool hardwareVerified = Verified();
            if (state == null && (T<Button>("DisableButton").IsEnabled || T<TextBlock>("SidebarStatus").Text != L.T("正在读取状态"))) throw new InvalidDataException("Unavailable driver state exposed a pause action or an untranslated status.");
            if (hardwareVerified) results.Add(L.T("启动时实际定位核验"));
            else results.Add("Hardware verification skipped: backend access or virtual GNSS is unavailable in this environment.");
            if (settings.Favorites.Count == 0 && (T<TextBlock>("FavoritesEmptyHint").Visibility != Visibility.Visible || T<Button>("LoadFavoriteButton").IsEnabled || T<Button>("ApplyFavoriteButton").IsEnabled || T<Button>("DeleteFavoriteButton").IsEnabled)) throw new InvalidDataException("Empty favorites state exposes unavailable actions.");
            T<TextBox>("SearchInput").Text = "W0°7'40.08\" N51°30'26.64\"";
            ShowPage("search"); await SearchAsync();
            if (T<ListBox>("SearchResults").Items.Count != 1) throw new InvalidDataException(L.T("界面坐标查询失败。"));
            T<Button>("UseSearchResultButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (CoordinateParser.Distance(ReadForm().Latitude, ReadForm().Longitude, 51.5074, -0.1278) > 0.01) throw new InvalidDataException(L.T("载入编辑坐标不匹配。"));
            results.Add(L.T("坐标搜索 → 选择结果 → 载入编辑"));
            T<TextBox>("NameInput").Text = L.T("界面测试收藏");
            int count = settings.Favorites.Count;
            T<Button>("SaveFavoriteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (settings.Favorites.Count != count + 1 || !T<Button>("LoadFavoriteButton").IsEnabled || !T<Button>("ApplyFavoriteButton").IsEnabled || T<TextBlock>("FavoritesEmptyHint").Visibility != Visibility.Collapsed) throw new InvalidDataException(L.T("保存收藏失败。"));
            T<ListBox>("FavoritesList").SelectedItem = settings.Favorites.Last();
            T<Button>("DeleteFavoriteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (settings.Favorites.Count != count) throw new InvalidDataException(L.T("移除收藏失败。"));
            if (count == 0 && (T<TextBlock>("FavoritesEmptyHint").Visibility != Visibility.Visible || T<Button>("LoadFavoriteButton").IsEnabled || T<Button>("ApplyFavoriteButton").IsEnabled || T<Button>("DeleteFavoriteButton").IsEnabled)) throw new InvalidDataException("Deleting the last favorite did not restore the empty state.");
            T<Button>("UndoFavoriteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (settings.Favorites.Count != count + 1 || store.Load().Favorites.Count != count + 1 || !T<Button>("DeleteFavoriteButton").IsEnabled || T<TextBlock>("FavoritesEmptyHint").Visibility != Visibility.Collapsed) throw new InvalidDataException(L.T("撤销收藏或持久化失败。"));
            T<ListBox>("FavoritesList").SelectedIndex = -1;
            if (T<Button>("LoadFavoriteButton").IsEnabled || T<Button>("ApplyFavoriteButton").IsEnabled || T<Button>("DeleteFavoriteButton").IsEnabled) throw new InvalidDataException("Favorite actions remained enabled without a selected item.");
            T<ListBox>("FavoritesList").SelectedIndex = 0;
            if (!T<Button>("LoadFavoriteButton").IsEnabled || !T<Button>("ApplyFavoriteButton").IsEnabled || !T<Button>("DeleteFavoriteButton").IsEnabled) throw new InvalidDataException("Favorite actions did not recover after selecting an item.");
            results.Add(L.T("收藏保存、移除、撤销与磁盘读回"));
            T<TextBox>("LatitudeInput").Text = "91";
            bool rejected = false; try { ReadForm(); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidDataException(L.T("界面接受了越界纬度。"));
            FillForm(LocationProfile.Example()); results.Add(L.T("界面拦截无效坐标"));
            var pageNames = new[] { "dashboard", "search", "favorites", "setup", "logs", "settings" };
            var panelNames = new[] { "DashboardPage", "SearchPage", "FavoritesPage", "SetupPage", "LogsPage", "SettingsPage" };
            var navNames = new[] { "NavDashboard", "NavSearch", "NavFavorites", "NavSetup", "NavLogs", "NavSettings" };
            foreach (string page in pageNames)
            {
                ShowPage(page); Window.UpdateLayout();
                int activeIndex = Array.IndexOf(pageNames, page);
                if (activeIndex < 0 || T<FrameworkElement>(panelNames[activeIndex]).Visibility != Visibility.Visible || panelNames.Where((name, index) => index != activeIndex && T<FrameworkElement>(name).Visibility == Visibility.Visible).Any()) throw new InvalidDataException("Page navigation visibility is inconsistent: " + page);
                var nav = T<Button>(navNames[activeIndex]); nav.ApplyTemplate();
                var indicator = nav.Template.FindName("NavIndicator", nav) as Border;
                if (indicator == null || indicator.Visibility != Visibility.Visible) throw new InvalidDataException("Active navigation marker is missing: " + page);
            }
            results.Add(L.T("六页导航与布局"));
            T<Grid>("RootSurface").ColumnDefinitions[0].Width = new GridLength(220);
            Window.UpdateLayout(); ApplySidebarLayout(220, true);
            if (sidebarCompact || T<TextBlock>("NavDashboardLabel").Visibility != Visibility.Visible || ToolTipService.GetIsEnabled(T<Button>("NavDashboard"))) throw new InvalidDataException("Expanded sidebar did not show labels or unexpectedly enabled icon tooltips.");
            CommitSidebarWidth(170);
            if (!sidebarCompact || T<Grid>("RootSurface").ColumnDefinitions[0].Width.Value != AppSettings.MinSidebarWidth || store.Load().SidebarWidth != AppSettings.MinSidebarWidth || T<TextBlock>("NavGroupLocation").Visibility != Visibility.Collapsed || T<Border>("SidebarGroupSeparator").Visibility != Visibility.Collapsed || T<TextBlock>("SidebarStatus").Visibility != Visibility.Collapsed) throw new InvalidDataException("Narrow sidebar did not snap to the compact icon rail.");
            for (int i = 0; i < SidebarNavNames.Length; i++)
            {
                var nav = T<Button>(SidebarNavNames[i]);
                var label = T<TextBlock>(SidebarLabelNames[i]);
                var tooltip = nav.ToolTip as ToolTip;
                var capsule = tooltip?.Content as Border;
                var caption = capsule?.Child as TextBlock;
                if (T<TextBlock>(SidebarIconNames[i]).Visibility != Visibility.Visible || label.Visibility != Visibility.Collapsed || !ToolTipService.GetIsEnabled(nav) || caption == null || caption.Text != label.Text || System.Windows.Automation.AutomationProperties.GetName(nav) != label.Text) throw new InvalidDataException("Compact sidebar icon capsule or accessible label is missing: " + SidebarNavNames[i]);
            }
            var focusTooltip = T<Button>("NavDashboard").ToolTip as ToolTip;
            OnSidebarNavGotKeyboardFocus(T<Button>("NavDashboard"), null);
            if (focusTooltip == null || !focusTooltip.IsOpen) throw new InvalidDataException("Compact sidebar label did not open on keyboard focus.");
            focusTooltip.IsOpen = false;
            AdjustSidebarWidthFromKey(Key.Right);
            if (settings.SidebarWidth != SidebarCompactThreshold + 16 || sidebarCompact) throw new InvalidDataException("Right-arrow keyboard resize did not expand the compact sidebar.");
            AdjustSidebarWidthFromKey(Key.Left);
            if (settings.SidebarWidth != SidebarCompactThreshold || sidebarCompact) throw new InvalidDataException("Left-arrow keyboard resize did not reduce the expanded sidebar.");
            AdjustSidebarWidthFromKey(Key.Left);
            if (settings.SidebarWidth != AppSettings.MinSidebarWidth || !sidebarCompact) throw new InvalidDataException("Left-arrow keyboard resize did not collapse the sidebar at the minimum.");
            AdjustSidebarWidthFromKey(Key.End);
            if (settings.SidebarWidth != AppSettings.MaxSidebarWidth || sidebarCompact) throw new InvalidDataException("End-key resize did not move the sidebar to maximum width.");
            AdjustSidebarWidthFromKey(Key.Home);
            if (settings.SidebarWidth != AppSettings.MinSidebarWidth || !sidebarCompact) throw new InvalidDataException("Home-key resize did not move the sidebar to minimum width.");
            string inputBeforeSwitch = T<TextBox>("LatitudeInput").Text + "|" + T<TextBox>("NameInput").Text;
            foreach (string language in new[] { "auto", "zh-CN", "zh-TW", "ja-JP", "en-US" })
            {
                SelectTag(T<ComboBox>("LanguageSelector"), language, "auto");
                ShowPage("settings");
                if (!IsAccessDenied(new UnauthorizedAccessException())) throw new InvalidDataException("Access-denied errors were not recognized for localization.");
                NoticeFromSource(AccessDeniedNoticeSource, true);
                ApplyLocale();
                if (lastNoticeText != L.T(AccessDeniedNoticeSource)) throw new InvalidDataException("The active access-denied notice did not follow the selected language: " + language);
                var noticeBar = T<Border>("NoticeBar");
                var headline = T<TextBlock>("NoticeHeadline");
                if (!noticeAwardMode || T<StackPanel>("NoticeStandardPanel").Visibility != Visibility.Collapsed || T<Grid>("NoticeAwardPanel").Visibility != Visibility.Visible || headline.Text != L.T("拒绝访问") || headline.TextAlignment != TextAlignment.Center || T<TextBlock>("NoticeAwardLabel").Text != L.T("喜报") || T<TextBlock>("NoticeOpenHint").Text != L.T("双击查看完整喜报") || T<TextBlock>("NoticeAwardTeaser").Text != L.T("Windows 拒绝了本次操作…") || System.Windows.Automation.AutomationProperties.GetName(noticeBar) != headline.Text) throw new InvalidDataException("Access-denied award notice is missing its localized, centered warning title or teaser: " + language);
                if (!SystemParameters.HighContrast && (!ReferenceEquals(T<Grid>("NoticeAwardPanel").Background, Brushes.Transparent) || headline.Effect == null)) throw new InvalidDataException("The localized access-denied headline must remain transparent over the celebratory image and use a readable shadow.");
                if (T<TextBlock>("PageTitle").Text != L.T("设置") || T<TextBlock>("AppearanceHeading").Text != L.T("外观与语言") || Convert.ToString(T<Button>("ApplyButton").Content) != L.T("应用并核验") || Convert.ToString(T<CheckBox>("NightModeToggle").Content) != L.T("夜间模式") || T<TextBlock>("NavGroupLocation").Text != L.T("工作区") || T<TextBlock>("NavGroupSystem").Text != L.T("系统") || state == null && (T<TextBlock>("SidebarStatus").Text != L.T("正在读取状态") || T<TextBlock>("HeroStatus").Text != L.T("读取驱动状态中")) || store.Load().Language != language) throw new InvalidDataException("Live language switch or control relocalization failed: " + language);
                for (int i = 0; i < SidebarNavNames.Length; i++)
                {
                    var nav = T<Button>(SidebarNavNames[i]);
                    var label = T<TextBlock>(SidebarLabelNames[i]);
                    var tip = nav.ToolTip as ToolTip;
                    var capsule = tip?.Content as Border;
                    var caption = capsule?.Child as TextBlock;
                    if (System.Windows.Automation.AutomationProperties.GetName(nav) != label.Text || caption?.Text != label.Text) throw new InvalidDataException("Sidebar capsule did not follow the active language: " + language);
                }
                if (inputBeforeSwitch != T<TextBox>("LatitudeInput").Text + "|" + T<TextBox>("NameInput").Text) throw new InvalidDataException("Language switch changed editor input.");
                Window.UpdateLayout();
                await Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                SaveVisual(T<FrameworkElement>("RootSurface"), Path.Combine(directory, "access-denied-" + language + ".png"));
            }
            results.Add("Auto, Simplified Chinese, Traditional Chinese, Japanese and English; control text and persistence");
            SelectTag(T<ComboBox>("LanguageSelector"), "zh-CN", "auto");
            ApplyLocale();
            Notice("A normal non-access warning notice.", true);
            if (noticeAwardMode || T<Grid>("NoticeAwardPanel").Visibility != Visibility.Collapsed || T<StackPanel>("NoticeStandardPanel").Visibility != Visibility.Visible) throw new InvalidDataException("A generic warning was incorrectly shown as a certificate.");
            var standardNotice = T<Border>("NoticeBar");
            if (standardNotice.Focusable || KeyboardNavigation.GetIsTabStop(standardNotice) || standardNotice.Cursor != Cursors.Arrow || System.Windows.Automation.AutomationProperties.GetName(standardNotice) != lastNoticeText || !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetHelpText(standardNotice)) || standardNotice.ToolTip != null) throw new InvalidDataException("A standard notice retained the certificate's click affordance or accessible label.");
            Notice(L.T("已保存到位置收藏。"));
            if (lastNoticeSource != "已保存到位置收藏。") throw new InvalidDataException("A localized standard notice did not retain its source key.");
            settings.Language = "auto"; L.Set("ja-JP"); ApplyLocale();
            if (lastNoticeText != L.T("已保存到位置收藏。")) throw new InvalidDataException("An existing standard notice did not follow the current locale.");
            SelectTag(T<ComboBox>("LanguageSelector"), "zh-CN", "auto"); ApplyLocale();
            NoticeFromSource(AccessDeniedNoticeSource, true);
            if (!SystemParameters.HighContrast && (!(T<Border>("NoticeBar").Background is ImageBrush) || !ReferenceEquals(T<Grid>("NoticeAwardPanel").Background, Brushes.Transparent))) throw new InvalidDataException("The compact access-denied teaser must preserve its original celebratory image behind a transparent panel.");
            HandleNoticeBarClickCount(1, false);
            if (activeNoticeCertificate != null) throw new InvalidDataException("A single click opened the full certificate.");
            noticeMusicSmokeOpened = new TaskCompletionSource<bool>();
            HandleNoticeBarClickCount(2, false);
            var certificate = activeNoticeCertificate;
            var certificateDetails = activeNoticeCertificateDetails;
            var certificateCloseButton = activeNoticeCertificateCloseButton;
            var certificateSurface = certificate?.Content as Grid;
            var certificateFrame = certificateSurface?.Children.OfType<Border>().FirstOrDefault();
            var certificateHeadline = certificateFrame?.Child is Border ? ((certificateFrame.Child as Border).Child as Grid) : null;
            var certificateHeadlinePanel = certificateHeadline?.Children.OfType<Border>().FirstOrDefault(panel => Grid.GetRow(panel) == 1);
            var certificateHeadlineText = certificateHeadlinePanel?.Child as TextBlock;
            if (certificate == null || !certificate.IsVisible || certificateSurface == null || !(certificateSurface.Background is SolidColorBrush) || certificateDetails == null || certificateDetails.Text != lastNoticeText || certificate.Title != L.T("完整喜报") || certificateHeadlineText == null || certificateHeadlineText.Text != L.T("拒绝访问") || certificateHeadlineText.TextAlignment != TextAlignment.Center) throw new InvalidDataException("Double-click did not open the complete Chinese certificate with its centered denial headline, opaque window backdrop and localized explanation.");
            if (!standardNotice.Focusable || !KeyboardNavigation.GetIsTabStop(standardNotice) || standardNotice.Cursor != Cursors.Hand || System.Windows.Automation.AutomationProperties.GetHelpText(standardNotice) != L.T("双击查看完整喜报")) throw new InvalidDataException("The access-denied notice did not restore its keyboard and accessible activation affordance.");
            if (!SystemParameters.HighContrast && (certificateHeadlinePanel.Background != Brushes.Transparent || certificateHeadlinePanel.BorderThickness != new Thickness(0) || certificateHeadlineText.Effect == null)) throw new InvalidDataException("The expanded denial headline must be centered over the original image without an opaque backing panel.");
            var certificateInner = certificateFrame?.Child as Border;
            var certificateLayout = certificateInner?.Child as Grid;
            var certificateDetailsPanel = certificateLayout?.Children.OfType<Border>().FirstOrDefault(panel => Grid.GetRow(panel) == 2);
            if (!SystemParameters.HighContrast && !(certificateInner?.Background is ImageBrush)) throw new InvalidDataException("The expanded certificate is missing its embedded celebratory image background.");
            RefreshNoticeCertificateAppearance(true);
            if (!ReferenceEquals(certificateSurface.Background, SystemColors.WindowBrush) || !ReferenceEquals(certificateInner.Background, SystemColors.WindowBrush) || !ReferenceEquals(certificateHeadlinePanel.Background, SystemColors.WindowBrush) || !ReferenceEquals(certificateHeadlineText.Foreground, SystemColors.WindowTextBrush) || !ReferenceEquals(certificateDetailsPanel?.Background, SystemColors.WindowBrush)) throw new InvalidDataException("The open certificate did not switch to high-contrast system colors.");
            RefreshNoticeCertificateAppearance(SystemParameters.HighContrast);
            if (!SystemParameters.HighContrast && (!(certificateSurface.Background is SolidColorBrush) || !(certificateInner.Background is ImageBrush) || certificateHeadlinePanel.Background != Brushes.Transparent)) throw new InvalidDataException("The certificate did not restore its image treatment and opaque backdrop after leaving high contrast.");
            settings.Language = "auto"; L.Set("ja-JP"); ApplyLocale();
            if (certificate.Title != L.T("完整喜报") || certificateHeadlineText.Text != L.T("拒绝访问") || certificateDetails.Text != lastNoticeText || Convert.ToString(certificateCloseButton.Content) != L.T("关闭此窗口")) throw new InvalidDataException("The open certificate did not update all text after a locale change: title=" + certificate.Title + "; headline=" + certificateHeadlineText.Text + "; details=" + certificateDetails.Text + "; expectedDetails=" + lastNoticeText + "; close=" + Convert.ToString(certificateCloseButton.Content) + "; expectedClose=" + L.T("关闭此窗口"));
            settings.Language = "auto"; L.Set("zh-CN"); ApplyLocale();
            if (certificateLayout == null || certificateLayout.Children.OfType<Button>().Count() != 1 || certificateLayout.Children.OfType<Slider>().Any() || certificateLayout.Children.OfType<Border>().Select(item => item.Child as TextBlock).Where(item => item != null).Any(item => item.Text.IndexOf("复古喜庆音乐自动播放中", StringComparison.Ordinal) >= 0)) throw new InvalidDataException("The certificate added an unexpected music status message or playback control.");
            Task musicOpened = await Task.WhenAny(noticeMusicSmokeOpened.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            if (musicOpened != noticeMusicSmokeOpened.Task || !noticeMusicSmokeOpened.Task.Result) throw new InvalidDataException("The embedded 8-bit certificate music did not open successfully in muted UI preview mode; signal=" + noticeMusicSmokeOpened.Task.Status + "; path=" + activeNoticeMusicPath + "; exists=" + (!string.IsNullOrEmpty(activeNoticeMusicPath) && File.Exists(activeNoticeMusicPath)) + "; source=" + (noticeMusicPlayer?.Source?.ToString() ?? "null") + "; audio=" + noticeMusicPlayer?.HasAudio + "; duration=" + (noticeMusicPlayer?.NaturalDuration.ToString() ?? "null") + "; logs=" + store.ReadLog());
            if (noticeMusicPlayer == null || noticeMusicPlayer.Volume != 0 || noticeMusicPlayer.IsMuted) throw new InvalidDataException("UI preview music was not safely muted.");
            if (CertificateMusicVolume(false) != 1.0 || CertificateMusicVolume(true) != 0.0) throw new InvalidDataException("Certificate music volume is not max in production or muted in preview tests.");
            string firstTrackPath = activeNoticeMusicPath;
            for (int trackIndex = 0; trackIndex < noticeMusicResources.Length; trackIndex++)
            {
                noticeMusicRequested = false;
                if (noticeMusicPlayer != null) { try { noticeMusicPlayer.Stop(); noticeMusicPlayer.Close(); } catch { } }
                noticeMusicPlayer = null;
                DeleteActiveNoticeMusicFile();
                CleanupNoticeMusicFiles();
                noticeMusicSmokeOpened = new TaskCompletionSource<bool>();
                noticeMusicRequested = true;
                previousNoticeMusicIndex = trackIndex;
                OpenNoticeMusicTrack(trackIndex);
                Task trackOpened = await Task.WhenAny(noticeMusicSmokeOpened.Task, Task.Delay(TimeSpan.FromSeconds(8)));
                if (trackOpened != noticeMusicSmokeOpened.Task || !noticeMusicSmokeOpened.Task.Result || noticeMusicPlayer == null || noticeMusicPlayer.Volume != 0 || noticeMusicPlayer.IsMuted) throw new InvalidDataException("Embedded 8-bit music track " + (trackIndex + 1) + " failed muted media-open validation.");
            }
            noticeMusicSmokeOpened = null;
            try { noticeMusicPlayer.Stop(); noticeMusicPlayer.Close(); } catch { }
            noticeMusicPlayer = null;
            DeleteActiveNoticeMusicFile();
            CleanupNoticeMusicFiles();
            if (firstTrackPath != null && File.Exists(firstTrackPath)) throw new InvalidDataException("The first temporary music track remained after the 20-track decode walk.");
            string musicFolder = Path.GetDirectoryName(firstTrackPath);
            string naturalEndFixture = Path.Combine(musicFolder, "smoke-natural-ended-" + Guid.NewGuid().ToString("N") + ".wav");
            WriteNoticeMusicSmokeTrack(naturalEndFixture);
            activeNoticeMusicPath = naturalEndFixture;
            noticeMusicTempFiles.Add(naturalEndFixture);
            noticeMusicSmokeOpened = new TaskCompletionSource<bool>();
            noticeMusicPlayer = CreateNoticeMusicPlayer();
            noticeMusicPlayer.Open(new Uri(naturalEndFixture, UriKind.Absolute));
            Task fixtureOpened = await Task.WhenAny(noticeMusicSmokeOpened.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            if (fixtureOpened != noticeMusicSmokeOpened.Task || !noticeMusicSmokeOpened.Task.Result) throw new InvalidDataException("The muted natural-end UI fixture did not open.");
            noticeMusicSmokeOpened = new TaskCompletionSource<bool>();
            Task nextMusicOpened = await Task.WhenAny(noticeMusicSmokeOpened.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            if (nextMusicOpened != noticeMusicSmokeOpened.Task || !noticeMusicSmokeOpened.Task.Result || activeNoticeMusicPath == firstTrackPath || activeNoticeMusicPath == naturalEndFixture || File.Exists(firstTrackPath) || File.Exists(naturalEndFixture) || !File.Exists(activeNoticeMusicPath)) throw new InvalidDataException("The actual MediaEnded event did not open a different embedded track and release the previous temporary files.");
            noticeMusicSmokeOpened = null;
            await Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            string certificateScreenshot = Path.Combine(directory, "access-denied-certificate.png");
            SaveVisual(certificate, certificateScreenshot);
            if (!IsFullyOpaqueScreenshot(certificateScreenshot)) throw new InvalidDataException("The certificate screenshot contains transparent pixels that display as black outside the rounded window surface.");
            string secondTrackPath = activeNoticeMusicPath;
            if (!HandleNoticeCertificateKey(certificate, Key.Escape) || certificate.IsVisible || activeNoticeCertificate != null) throw new InvalidDataException("Escape did not close the certificate window.");
            if (noticeMusicPlayer != null || activeNoticeMusicPath != null || File.Exists(secondTrackPath)) throw new InvalidDataException("Closing the certificate did not release its music player and temporary track.");
            HandleNoticeBarKey(Key.Enter, false);
            if (activeNoticeCertificate == null || !activeNoticeCertificate.IsVisible) throw new InvalidDataException("Keyboard activation did not open the full certificate.");
            certificateCloseButton = activeNoticeCertificateCloseButton;
            certificateCloseButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (activeNoticeCertificate != null) throw new InvalidDataException("The certificate close button did not close the window.");
            var closeModalCertificate = Window.Dispatcher.InvokeAsync(() => activeNoticeCertificate?.Close(), DispatcherPriority.ContextIdle).Task;
            var routedKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(standardNotice), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent };
            standardNotice.RaiseEvent(routedKey);
            await closeModalCertificate;
            if (!routedKey.Handled || activeNoticeCertificate != null) throw new InvalidDataException("The actual keyboard event route did not open and dismiss the modal certificate.");
            results.Add("Access-denied teaser with transparent title over the original image, centered animated gold title, localized detail, all 20 embedded tracks media-open in muted mode, natural MediaEnded random-track advance, double-click and keyboard open, Escape and close-button dismissal");
            CommitSidebarWidth(276);
            if (sidebarCompact || settings.SidebarWidth != 276 || Math.Abs(store.Load().SidebarWidth - 276) > 0.01 || T<TextBlock>("NavDashboardLabel").Visibility != Visibility.Visible || ToolTipService.GetIsEnabled(T<Button>("NavDashboard"))) throw new InvalidDataException("Sidebar did not expand or persist its chosen width.");
            results.Add("Resizable sidebar, compact icon rail, capsule labels, keyboard resizing and persisted width");
            T<CheckBox>("SystemThemeToggle").IsChecked = false;
            T<CheckBox>("NightModeToggle").IsChecked = true;
            if (settings.ThemeMode != "dark" || ((SolidColorBrush)Window.Resources["AppBg"]).Color != (Color)ColorConverter.ConvertFromString("#151D22")) throw new InvalidDataException("Dark theme did not apply.");
            T<CheckBox>("NightModeToggle").IsChecked = false;
            if (settings.ThemeMode != "light" || ((SolidColorBrush)Window.Resources["AppBg"]).Color != (Color)ColorConverter.ConvertFromString("#F2F6F4")) throw new InvalidDataException("Light theme did not apply.");
            T<CheckBox>("SystemThemeToggle").IsChecked = true;
            if (settings.ThemeMode != "system") throw new InvalidDataException("System theme mode did not persist.");
            if (systemBackdropApplied != (ReferenceEquals(Window.Background, Brushes.Transparent) && ReferenceEquals(T<Grid>("RootSurface").Background, Brushes.Transparent))) throw new InvalidDataException("Native window backdrop and content surface are inconsistent.");
            results.Add(systemBackdropApplied ? "Native Windows system backdrop with theme-aware fallback" : "Solid theme-aware backdrop fallback");
            SelectTag(T<ComboBox>("AccentSelector"), "ocean", "mint");
            T<Slider>("FontScaleSlider").Value = 110;
            if (settings.AccentColor != "ocean" || settings.FontScale != 110 || T<TextBlock>("FontScaleValue").Text != "110%") throw new InvalidDataException("Appearance settings did not apply.");
            results.Add("Light, dark, system theme, accent selection and font scaling");
            File.WriteAllText(Path.Combine(directory, "ui-smoke.json"), Json.To(new { passed = true, hardwareVerified, results }), new System.Text.UTF8Encoding(false));
            closing = true; Window.Close();
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
