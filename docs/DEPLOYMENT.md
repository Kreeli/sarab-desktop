# One-click startup and packaging / 一键启动与打包

## End users / 使用发布包

Download the complete installer or portable ZIP from the project's releases. Extract the ZIP fully, then double-click `Start.cmd`. It checks Windows version, CPU architecture, .NET Framework and every file listed in the package checksum manifest before launching. No Visual Studio, SDK, Node.js, Python, internet connection or media codecs are needed for portable GUI startup and embedded PCM music playback. Search providers still require internet; Windows location and driver operations still require their documented prerequisites.

下载完整安装包或便携 ZIP。将 ZIP 完整解压，再双击 `Start.cmd`；启动器会检查系统、架构、.NET Framework 和发行文件完整性后打开应用。便携界面与内置 PCM 音乐启动不需要开发工具、联网或媒体编解码器。地址搜索需要联网，系统定位与驱动操作仍需满足安装指南中的条件。

Supported: **Windows 10 1809+ or Windows 11, x64 (AMD64), .NET Framework 4.7.2+**. ARM64, x86, Linux, macOS, Windows Server Core and systems without a desktop/audio device are not promised. The launcher reports missing prerequisites; it does not silently install runtimes or weaken Windows security. PowerShell 5.1 is included with supported desktop Windows; an organization that blocks scripts can launch `SarabDesktop.exe` directly after checking the package.

支持 Windows 10 1809+ / Windows 11 x64（AMD64），需要 .NET Framework 4.7.2 或更高版本。不支持 ARM64、32 位、Linux、macOS 或 Server Core。无音频设备时音乐不可用，主界面仍应可运行。组织策略禁止脚本时，可以校验发布包后直接打开 `SarabDesktop.exe`。

The launcher uses a **process-only** PowerShell execution-policy setting; it does not persist a policy change. SHA256SUMS detects damage and incomplete extraction, not publisher identity: obtain both archive and hashes from a trusted project release. Music is embedded in the application, and no separate WAV folder is needed on the user's PC.

The upstream GNSS driver is test-signed. A generic one-click launcher cannot make it work on every Windows configuration. Driver installation requires explicit administrator approval and the documented manual Secure Boot/test-signing preparation. The launcher neither modifies boot settings nor installs drivers. Portable folders cannot be used for elevated operations: install the app in protected Program Files first. See [installation guide](INSTALL.md) / [安装说明](INSTALL.zh-CN.md).

Packages also contain the Microsoft-signed Visual C++ x64 runtime: an application-local DLL alongside the upstream CLI and the complete offline redistributable under `runtime/`. The full Setup installs the system runtime before installing the GUI, using `/install /quiet /norestart`; it accepts success, an already-newer runtime (1638), or a manual restart request (3010), and stops on other failures. No runtime installation is performed by portable startup or by the build. The UMDF driver requires the system runtime; an app-local CLI DLL does not satisfy that driver requirement. Building verifies the offline installer's fixed version, SHA256 and Microsoft signature, and the app-local DLL's Microsoft signature. Cached verified dependencies avoid repeated downloads. The original driver files and signed catalog are left unchanged.

发行包已离线内置微软签名的 Visual C++ x64 运行库：CLI 旁附本地 DLL，`runtime/` 中附完整运行库安装器。完整 Setup 会先安装系统运行库，成功或已有新版时继续；需要重启时提示用户手动重启，不主动重启。便携启动和源码构建不会在本机安装运行库。驱动仍需系统运行库，不能用 CLI 旁的 DLL 代替。

## Source checkout / 源码运行

Elevated setup state is stored in protected `ProgramData/SarabDesktop/Setup` directories owned by Administrators or SYSTEM. Pre-existing user-owned directories, writable ACLs, reparse points and linked state files are rejected. An administrator must inspect any legacy/custom state directory before restoring trusted ownership and permissions; the app does not follow links or automatically rewrite an untrusted directory's ACL.

管理员安装状态目录要求由 Administrators 或 SYSTEM 所有，且不允许普通用户写入。预先创建的用户所有目录、重解析点及链接文件会被拒绝；旧版或自定义目录应由管理员检查后恢复可信权限，程序不会自动修改不可信路径的权限。

Double-click the same root `Start.cmd`. If `dist/SarabDesktop.exe` is absent, it builds once and opens the resulting package. Install Visual Studio Build Tools with Desktop development with C++ and a Windows SDK including C++/WinRT first. The initial build downloads the hash-pinned upstream archive if it is not cached in `vendor/`; later cached builds can run offline. An existing `dist` is verified and launched; explicitly run `Build.ps1` after editing sources.

源码目录中双击 `Start.cmd`：没有现成 `dist` 时，先构建再启动。首次源码构建需要 C++ Build Tools、Windows SDK，以及联网获取固定依赖；已缓存依赖后可离线构建。已有 `dist` 时只校验并启动它，修改源码后请显式重新构建。

## Packaging / 打包

```powershell
# Build and create both installer and portable ZIP (requires Inno Setup 6.5+).
.\scripts\Package.ps1
# Build and create only the portable ZIP, without Inno Setup.
.\scripts\Package.ps1 -PortableOnly
# Package the already built and tested distribution, validating its hashes first.
.\scripts\Package.ps1 -SkipBuild
# Check prerequisites and integrity without launching or installing anything.
.\scripts\Start-SarabDesktop.ps1 -ProjectRoot .\dist -CheckOnly
```

CI builds once, runs self-tests on that exact distribution, packages it without rebuilding, then extracts and checks the portable archive. Checksums are generated only for artifacts created by the current packaging mode, so stale installers are not accidentally included in a portable-only manifest. These checks do not replace real driver integration, UAC, installer upgrade/uninstall, Secure Boot and device tests on a supported physical Windows PC.
