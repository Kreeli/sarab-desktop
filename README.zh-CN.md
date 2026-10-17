# Sarab Desktop

[English](README.md) | 简体中文

**为 [TheMRVX 的 Sarab 原项目](https://github.com/TheMRVX/sarab) 制作的独立 Windows 桌面 GUI。** 虚拟 GNSS 驱动和定位核心由 Sarab 提供；本项目新增桌面控制界面、中英日繁四语支持和安装引导，并非上游官方发布。

## 下载与安装

在 [Releases](https://github.com/Kreeli/sarab-desktop/releases) 下载 **`SarabDesktop-1.1.0-Setup-x64.exe`**。安装程序需要管理员确认，并将应用安装到受保护的 Program Files 目录，避免普通用户进程替换驱动工具。便携 ZIP 可用于普通权限功能，但不能执行驱动安装或位置写入等管理员操作。

便携版完整解压后，双击 **`Start.cmd`** 即可检查 Windows 版本、x64 架构、.NET Framework 和文件完整性并启动。音乐内嵌在应用中，直接从内存播放 PCM，无需联网下载、外部播放器或可选媒体组件。支持范围及源码首次运行方式见[一键启动与打包说明](docs/DEPLOYMENT.md)。

1. 运行安装程序，选择中文或英文，然后打开上位机。
2. 进入**安装与修复**，检查系统架构、Secure Boot、当前测试签名、驱动和位置权限。
3. 原版驱动使用测试签名。如需准备系统，先阅读[安装指南](docs/INSTALL.zh-CN.md)，完成准备后手动重启，再回到向导安装驱动。固件及磁盘保护需要安装者明确处理；程序不会自动更改固件、解密磁盘或重启电脑。
4. 手动允许 Windows 定位权限。在概览中设置自己的经纬度，点击**应用并核验**。新安装的驱动默认暂停虚拟定位，直到你主动应用坐标。

支持 **Windows 10 1809+ / Windows 11 的 x64 电脑**，不支持 ARM64。安装驱动、修改定位需要管理员授权；主界面和登录自启动以普通权限运行。由于上游驱动仍是测试签名，无法保证所有原装 Windows 11 电脑都能零配置安装。

## 功能

- 在设置中即时切换**自动跟随系统、简体中文、繁體中文、日本語和 English**，自动记住选择；保留自己的地点名称和已有日志。
- 选择跟随 Windows、日间或夜间外观，更换强调色并将界面缩放到 90–125%；设置立即生效并保存。
- 支持经纬度、度分秒、Google Maps 坐标链接与手动地址查询。默认 OpenStreetMap / Nominatim；Google 地址 API 使用自己的 Key。
- 应用与暂停虚拟位置，设置海拔、精度和漂移；独立读取 Windows Geolocator 核验，不把配置开关当作生效证明。
- 收藏导入导出、撤销移除、本地日志、系统托盘和可选登录自启动（默认关闭）。
- 安装条件检查、固定驱动文件校验、分步骤安装和驱动卸载。

公网 IP 由 VPN 等独立控制。GUI 控制 Sarab 接入的 Windows 定位来源，应用仍可能使用自己的 IP、Wi-Fi 或账户数据，系统读回成功不代表覆盖所有应用。

## 数据与升级

配置和日志保存在 `%LOCALAPPDATA%\SarabDesktop`。Key 使用 Windows DPAPI 按当前用户加密，不写入日志或收藏导出。地址只在手动查询时发给所选服务，不做自动补全或静默切换；除短链接跳转外，坐标解析可离线进行。

公开安装包仅含未应用的示例地点，**不包含开发电脑的私人位置、收藏、Key 或日志**。从 1.0 升级会保留本机配置。关闭窗口默认进入托盘；退出上位机后驱动继续运行。**卸载 GUI 也会保留驱动**，如需移除，请先在安装与修复中单独卸载；之后系统可能恢复真实位置来源。证书及测试模式清理方法见指南。

## 构建

开发机需要 Visual Studio C++ Build Tools 和包含 C++/WinRT 的 Windows SDK。PowerShell 执行：

```powershell
.\Build.ps1
.\scripts\Package.ps1 -Iscc 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

自定义 VS 路径可用 `-VisualStudio`。构建脚本依据 `dependencies.lock.json` 下载并校验固定上游版本，以静态 C++ 运行库构建独立读取器，并直接引用 Windows .NET Framework 编译 WPF，不需要 NuGet。Inno Setup 6.5+ 负责安装包与便携 ZIP；使用者不需要开发环境。

详见[贡献说明](CONTRIBUTING.md)、[贡献与署名](CREDITS.md)、[许可证](THIRD-PARTY-NOTICES.md)和[与上游的关系](docs/UPSTREAM.md)。Windows CI 不安装驱动或修改启动配置。`--self-test <报告路径>` 可运行基础检查；`--render <目录> --language zh-CN` 导出界面；`--ui-smoke <目录>` 使用隔离配置验证界面流程，实际定位部分要求已有正常运行的驱动。

## 许可与原作者贡献

新增 GUI 和配套代码使用 MIT，© 2026 Kreeli 与贡献者。**原 Sarab v1.0.0 使用 MIT，© 2026 TheMRVX**，保留完整原始许可证。上游当前 main 分支为 Apache-2.0，本版不包含该新分支的代码。再分发时应保留 [NOTICE](NOTICE) 与第三方许可证。[访问 Sarab 原项目 →](https://github.com/TheMRVX/sarab)
