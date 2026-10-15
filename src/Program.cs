using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SarabDesktop
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                string languageOverride = null;
                var arguments = new List<string>(args);
                int languageIndex = arguments.IndexOf("--language");
                if (languageIndex >= 0 && languageIndex + 1 < arguments.Count) { languageOverride = arguments[languageIndex + 1]; L.Set(languageOverride); arguments.RemoveRange(languageIndex, 2); }
                args = arguments.ToArray();
                if (args.Length == 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
                if (args.Length >= 2 && args[0] == "--check-backend") return SelfTests.CheckBackendAsync(args[1]).GetAwaiter().GetResult();
                if (args.Length == 2 && args[0] == "--search-smoke") return SelfTests.SearchSmokeAsync(args[1]).GetAwaiter().GetResult();
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                if (args.Length == 2 && (args[0] == "--render" || args[0] == "--ui-smoke"))
                {
                    application.Startup += async (s, e) =>
                    {
                        try
                        {
                            using (var controller = new DesktopController(true, Path.Combine(args[1], "preview-data-" + DateTime.Now.ToString("yyyyMMddHHmmssfff")), languageOverride))
                                if (args[0] == "--render") await controller.RenderAsync(args[1]); else await controller.UiSmokeAsync(args[1]);
                            application.Shutdown(0);
                        }
                        catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "render-error.txt"), ex.ToString()); application.Shutdown(1); }
                    };
                    return application.Run();
                }
                string identity = WindowsIdentity.GetCurrent().User.Value;
                bool created;
                using (var mutex = new Mutex(true, "Local\\SarabDesktop.Instance." + identity, out created))
                using (var appMarker = new Mutex(false, "SarabDesktop.App"))
                using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\SarabDesktop.Activate." + identity))
                {
                    if (!created) { signal.Set(); return 0; }
                    DesktopController controller = null;
                    RegisteredWaitHandle activation = null;
                    application.Startup += async (s, e) =>
                    {
                        controller = new DesktopController(false, null, languageOverride);
                        application.MainWindow = controller.Window;
                        activation = ThreadPool.RegisterWaitForSingleObject(signal, (o, timedOut) => application.Dispatcher.BeginInvoke(new Action(() => controller.ShowWindow())), null, -1, false);
                        if (Array.IndexOf(args, "--tray") < 0) controller.ShowWindow();
                        await controller.InitializeAsync();
                        if (Array.IndexOf(args, "--setup") >= 0) await controller.OpenSetupAsync();
                    };
                    application.DispatcherUnhandledException += (s, e) =>
                    {
                        e.Handled = true;
                        MessageBox.Show(e.Exception.Message, L.T("Sarab Desktop · 操作未完成"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    };
                    application.Exit += (s, e) => { activation?.Unregister(null); controller?.Dispose(); };
                    int result = application.Run();
                    mutex.ReleaseMutex();
                    return result;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, L.T("Sarab Desktop · 启动失败"), MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
