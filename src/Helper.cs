using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

namespace SarabDesktop
{
    public static class HelperProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 2 && args[0] == "--language") { L.Set(args[1]); args = args.Skip(2).ToArray(); }
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException(L.T("设置位置需要管理员授权。"));
                if (args.Length == 1 && args[0] == "--prepare-system") { Installation.PrepareTestSigning(); return 0; }
                if (args.Length == 1 && args[0] == "--install-driver") { Installation.InstallDriver(); return 0; }
                if (args.Length == 1 && args[0] == "--remove-driver") { Installation.RemoveDriver(); return 0; }
                Backend.VerifyFile(Backend.SarabPath, Backend.SarabHash);
                string command;
                if (args.Length == 7 && args[0] == "--apply")
                {
                    var p = new LocationProfile { Latitude = Parse(args[1]), Longitude = Parse(args[2]), Altitude = Parse(args[3]), Accuracy = Parse(args[4]), Drift = Parse(args[5]) == 1, DriftRadius = Parse(args[6]) };
                    p.Validate();
                    var state = Backend.ReadStateAsync().GetAwaiter().GetResult();
                    if (!state.Installed || !state.Healthy) throw new InvalidOperationException(L.T("没有找到正常工作的 Sarab 虚拟 GNSS 驱动，请先安装或修复驱动。"));
                    command = Backend.SarabSetArguments(p);
                }
                else if (args.Length == 1 && args[0] == "--disable") command = "disable";
                else throw new ArgumentException(L.T("不支持的管理员操作。"));
                using (var process = Process.Start(new ProcessStartInfo(Backend.SarabPath, command) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Backend.SarabPath), RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(20000)) { try { process.Kill(); } catch { } throw new TimeoutException(L.T("驱动配置超时。")); }
                    if (process.ExitCode != 0) throw new InvalidOperationException(L.T("Sarab 返回错误：") + error.GetAwaiter().GetResult());
                    output.GetAwaiter().GetResult();
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, L.T("Sarab Desktop · 操作未完成"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }
        static double Parse(string input) => double.Parse(input, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
