using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SarabDesktop
{
    public sealed class PreflightState
    {
        public bool IsAmd64 { get; set; }
        public int WindowsBuild { get; set; }
        public bool? SecureBoot { get; set; }
        public bool TestSigning { get; set; }
        public bool? SystemDiskReadyForBootChange { get; set; }
        public bool? MemoryIntegrity { get; set; }
        public DriverState Driver { get; set; }
    }
    public static class InstallationPolicy
    {
        public static string PreparationBlockReason(PreflightState state)
        {
            if (!state.IsAmd64 || state.WindowsBuild < 17763) return L.T("需要 Windows 10 1809 或更新版本的 x64 系统；不支持 ARM64 驱动安装。");
            if (state.TestSigning) return null;
            if (state.SecureBoot != false) return L.T("请先按电脑厂商说明在 UEFI 中关闭 Secure Boot，再重新检测。程序不会修改固件设置。");
            if (state.SystemDiskReadyForBootChange != true) return L.T("系统盘保护未暂停或无法确认磁盘状态，已停止修改启动配置。请先备份恢复密钥并阅读安装指南，完成所需的磁盘保护准备后再试。");
            return null;
        }
    }
    public static class Installation
    {
        public const string CertificateThumbprint = "40BDA793F3AE6F6A12022D05497D43D9154793EE";
        public static readonly Dictionary<string, string> DriverHashes = new Dictionary<string, string>
        {
            ["SarabGnss.dll"] = "F7F09AE07D45B85B16B758D012BFFD0D54FC1E930C14121D42AE20FD90C9397F",
            ["SarabGnss.cat"] = "C0F53FE5EA71FED3DF09565157961ED7DE97D085FBDC1B1767C89C732DC39AC2",
            ["SarabGnss.inf"] = "37C74DEDDCC3DBB1CF5D7F9679F4EFBB98DEF4A099158A002B8C30A4989855F5",
            ["SarabGnss.cer"] = "73A183197D904F217C78241DBEC8A5881394EE314FD0BA5079A4A90B52049842"
        };
        public static string DriverDirectory => Path.Combine(Backend.BaseDirectory, "driver");
        public static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SarabDesktop", "Setup");
        public static void VerifyDriverPackage()
        {
            Backend.VerifyFile(Backend.SarabPath, Backend.SarabHash);
            foreach (var pair in DriverHashes) Backend.VerifyFile(Path.Combine(DriverDirectory, pair.Key), pair.Value);
            using (var certificate = new X509Certificate2(Path.Combine(DriverDirectory, "SarabGnss.cer")))
                if (!string.Equals(certificate.Thumbprint, CertificateThumbprint, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.T("驱动测试证书与固定版本不匹配。"));
        }
        public static async Task<PreflightState> ReadPreflightAsync(bool inspectEncryption = false)
        {
            var state = new PreflightState { Driver = await Backend.ReadStateAsync() };
            SYSTEM_INFO info; GetNativeSystemInfo(out info); state.IsAmd64 = info.Architecture == 9;
            using (var version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) { int build; int.TryParse(Convert.ToString(version?.GetValue("CurrentBuildNumber")), out build); state.WindowsBuild = build; }
            using (var secure = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State")) { var value = secure?.GetValue("UEFISecureBootEnabled"); if (value != null) state.SecureBoot = Convert.ToInt32(value) != 0; }
            if (state.SecureBoot == null) { uint firmware; if (GetFirmwareType(out firmware) && firmware == 1) state.SecureBoot = false; }
            using (var hvci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity")) { var value = hvci?.GetValue("Enabled"); if (value != null) state.MemoryIntegrity = Convert.ToInt32(value) != 0; }
            state.TestSigning = state.Driver.TestSigning;
            if (inspectEncryption)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        string drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');
                        using (var search = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftVolumeEncryption", "SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter='" + drive + "'"))
                        using (var volumes = search.Get())
                            foreach (ManagementObject volume in volumes)
                                using (var protection = volume.InvokeMethod("GetProtectionStatus", null, null))
                                using (var conversion = volume.InvokeMethod("GetConversionStatus", null, null))
                                    if (Convert.ToUInt32(protection["ReturnValue"]) == 0 && Convert.ToUInt32(conversion["ReturnValue"]) == 0) state.SystemDiskReadyForBootChange = Convert.ToUInt32(protection["ProtectionStatus"]) == 0 && (Convert.ToUInt32(conversion["ConversionStatus"]) == 0 || Convert.ToUInt32(conversion["ConversionStatus"]) == 1);
                    }
                    catch { state.SystemDiskReadyForBootChange = null; }
                });
            }
            return state;
        }
        static void EnsureStateDirectory()
        {
            Directory.CreateDirectory(StateDirectory);
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid }) security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(StateDirectory).SetAccessControl(security);
        }
        public static void PrepareTestSigning()
        {
            var state = ReadPreflightAsync(true).GetAwaiter().GetResult();
            string reason = InstallationPolicy.PreparationBlockReason(state); if (reason != null) throw new InvalidOperationException(reason);
            if (state.TestSigning) return;
            EnsureStateDirectory();
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            string backup = Path.Combine(StateDirectory, "bcd-" + stamp + ".bak");
            RunSystemTool("bcdedit.exe", "/export \"" + backup + "\"", 20000);
            File.WriteAllText(Path.Combine(StateDirectory, "boot-" + stamp + ".json"), Json.To(new { createdUtc = DateTime.UtcNow.ToString("o"), backup, automaticRestart = false, operation = "EnableTestSigning", originallyActive = state.TestSigning }), new UTF8Encoding(false));
            RunSystemTool("bcdedit.exe", "/set {current} testsigning on", 20000);
        }
        public static void InstallDriver()
        {
            VerifyDriverPackage();
            var state = ReadPreflightAsync().GetAwaiter().GetResult();
            if (!state.IsAmd64 || state.WindowsBuild < 17763) throw new InvalidOperationException(L.T("不支持当前系统架构或 Windows 版本。"));
            if (state.Driver.Healthy) return;
            if (!state.TestSigning) throw new InvalidOperationException(L.T("测试签名尚未在本次启动中生效。请完成准备步骤、手动重启，然后重新打开安装引导。"));
            EnsureStateDirectory();
            string snapshotPath = Path.Combine(StateDirectory, "driver-before.json");
            if (!File.Exists(snapshotPath)) File.WriteAllText(snapshotPath, Json.To(new { createdUtc = DateTime.UtcNow.ToString("o"), rootExisted = CertificateExists(StoreName.Root), publisherExisted = CertificateExists(StoreName.TrustedPublisher), driverExisted = state.Driver.Installed, certificateThumbprint = CertificateThumbprint }), new UTF8Encoding(false));
            using (var key = Registry.LocalMachine.CreateSubKey(Backend.RegistryPath)) key.SetValue("Enabled", 0, RegistryValueKind.DWord);
            RunTool(Backend.SarabPath, "driver install --inf \"" + Path.Combine(DriverDirectory, "SarabGnss.inf") + "\"", 90000);
            DriverState after = null;
            for (int i = 0; i < 8; i++) { after = Backend.ReadStateAsync().GetAwaiter().GetResult(); if (after.Healthy) break; Thread.Sleep(1500); }
            if (after == null || !after.Healthy) throw new InvalidOperationException(L.T("未检测到正常工作的 Sarab 设备，未将安装器输出当成成功证明。请检查设备管理器，必要时手动重启。") + " (" + after?.ProblemCode + ")");
            if (!CertificateExists(StoreName.Root) || !CertificateExists(StoreName.TrustedPublisher)) throw new InvalidOperationException(L.T("驱动证书存储检查失败，请查看安装日志。"));
        }
        public static void RemoveDriver()
        {
            EnsureStateDirectory();
            var devices = new List<string>(); var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var search = new ManagementObjectSearcher("SELECT DeviceID,HardwareID FROM Win32_PnPEntity"))
            using (var results = search.Get())
                foreach (ManagementObject item in results) if ((item["HardwareID"] as string[])?.Any(id => string.Equals(id, @"Root\SarabGnss", StringComparison.OrdinalIgnoreCase)) == true) devices.Add(Convert.ToString(item["DeviceID"]));
            using (var search = new ManagementObjectSearcher("SELECT DeviceID,InfName FROM Win32_PnPSignedDriver"))
            using (var results = search.Get())
                foreach (ManagementObject item in results) if (devices.Contains(Convert.ToString(item["DeviceID"]), StringComparer.OrdinalIgnoreCase))
                {
                    string name = Convert.ToString(item["InfName"]);
                    if (Regex.IsMatch(name, @"^oem\d+\.inf$", RegexOptions.IgnoreCase))
                    {
                        string inf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", name);
                        if (!File.ReadAllText(inf).Contains(@"Root\SarabGnss")) throw new InvalidDataException(L.T("驱动包标识不匹配，已停止卸载。"));
                        packages.Add(name);
                    }
                }
            using (var key = Registry.LocalMachine.OpenSubKey(Backend.RegistryPath, true)) key?.SetValue("Enabled", 0, RegistryValueKind.DWord);
            foreach (string id in devices) RunSystemTool("pnputil.exe", "/remove-device \"" + id + "\"", 30000);
            foreach (string package in packages) RunSystemTool("pnputil.exe", "/delete-driver " + package + " /uninstall", 30000);
            if (Backend.ReadStateAsync().GetAwaiter().GetResult().Installed) throw new InvalidOperationException(L.T("仍检测到 Sarab 设备，请检查设备管理器。"));
            // Keep shared trust and global boot settings intact. They may be used by another installation.
            File.AppendAllText(Path.Combine(StateDirectory, "operations.log"), DateTime.UtcNow.ToString("o") + " Removed Sarab devices; retained certificate and boot configuration.\r\n");
        }
        static bool CertificateExists(StoreName name) { using (var store = new X509Store(name, StoreLocation.LocalMachine)) { store.Open(OpenFlags.ReadOnly); return store.Certificates.Find(X509FindType.FindByThumbprint, CertificateThumbprint, false).Count > 0; } }
        static void RunSystemTool(string name, string arguments, int timeout) => RunTool(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name), arguments, timeout);
        static void RunTool(string path, string arguments, int timeout)
        {
            try { string output = Backend.RunAsync(path, arguments, timeout).GetAwaiter().GetResult(); File.AppendAllText(Path.Combine(StateDirectory, "operations.log"), DateTime.UtcNow.ToString("o") + " " + Path.GetFileName(path) + "\r\n" + output + "\r\n", new UTF8Encoding(false)); }
            catch (Exception ex) { File.AppendAllText(Path.Combine(StateDirectory, "operations.log"), DateTime.UtcNow.ToString("o") + " ERROR " + ex.Message + "\r\n"); throw; }
        }
        [StructLayout(LayoutKind.Sequential)] struct SYSTEM_INFO { public ushort Architecture, Reserved; public uint PageSize; public IntPtr Minimum, Maximum, Mask; public uint ProcessorCount, ProcessorType, AllocationGranularity; public ushort Level, Revision; }
        [DllImport("kernel32.dll")] static extern void GetNativeSystemInfo(out SYSTEM_INFO info);
        [DllImport("kernel32.dll")] static extern bool GetFirmwareType(out uint type);
    }
}
