# Validation / 验证范围

## 1.1.0 · 2026-10-16

Validated on an x64 Windows 11 machine with an already installed and functioning Sarab v1.0.0 driver.

| Check | Result |
| --- | --- |
| Build desktop GUI, helper and static-runtime native reader | Passed |
| Required UI/message translation coverage | 289 source strings checked; 290 English entries |
| Functional and installation-policy self-tests | 22 passed |
| Live English ↔ Chinese switching and persistence | Passed |
| Preserve editor input and favorite data when switching | Passed |
| Coordinate parsing, validation, favorites and undo UI flow | Passed with isolated settings |
| Actual Windows Geolocator readback | Passed; matched the active configured virtual location |
| English and Chinese WPF rendering | Inspected |
| Installer file deployment and SHA256 comparison | Passed in an isolated target folder |
| Run self-tests from the installed copy | Passed |
| GUI uninstallation and registration cleanup | Passed |
| Preserve real user settings and active driver configuration during install/uninstall test | Passed |
| Fixed driver DLL, catalog, INF, CLI and certificate identity | Verified |

The installation-policy tests cover architecture/version rejection, Secure Boot on/unknown, active/unknown disk protection, eligible preparation state and an already active test mode. They do not change actual firmware, disk protection or boot settings.

**Not validated on a fresh physical computer/VM:** the new wizard's real first-time BCD modification, certificate import, PnP installation/removal, reboot/resume and behavior under every OEM/enterprise policy. The underlying pinned driver was installed and exercised separately on the development machine, but that does not replace a clean-machine test of the new wizard. Google Geocoding networking was not tested with a real API key.

Private diagnostic logs, screenshots and actual user coordinates are intentionally excluded from this public repository and release packages. A successful verification applies to the sampled Windows location result; it does not establish coverage of every application's independent location sources.

## 简体中文

在已有正常 Sarab 驱动的 Windows 11 x64 电脑验证了：22 项基础与安装策略检查、289 项必需翻译、中英即时切换及持久化、输入内容保留、收藏操作、真实 Windows 定位读回，以及隔离目录中的 GUI 安装、文件哈希核对和卸载。安装卸载测试保留了实际用户配置和当前驱动坐标。

本轮没有在全新实体电脑或虚拟机中实际修改 BCD、导入证书、安装/卸载 PnP 驱动或测试重启/休眠。原版驱动此前已在开发机安装并实测，但不能代替新向导的干净系统验证。Google 地址 API 没有真实 Key，未作联网测试。私人日志、坐标和截图不公开。
