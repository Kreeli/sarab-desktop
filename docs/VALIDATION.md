# Validation / 验证范围

## PR #1 additional audit · 2026-10-16

The follow-up audit on Windows x64 validated the following changes without installing/removing a driver or modifying boot settings:

- Desktop/helper/native-reader build; 327 source strings and 334 entries in each embedded translation dictionary.
- 41 self-test cases: all passed in the normal user context. In the restricted sandbox, 40 passed and the temporary HKCU Run write test was skipped because the host denied access. Includes malicious ACLs, raw generic-access ACEs, bounded UTF-8 configuration reads/writes, large log tails, malformed network bodies and a stream that ignores cancellation. Atomic settings replacement was reproduced failing without metadata-merge compatibility and passed after the fix.
- UI smoke tests in zh-CN, zh-TW, ja-JP and en-US passed, including all 20 embedded tracks opened through native waveOut in muted preview, actual end-of-buffer random advancement, teardown, sidebar resizing, locale changes, high contrast and certificate screenshots.
- Distribution integrity tests cover a valid fixture plus seven rejection cases: changed executable, omitted executable, path traversal, duplicate path, canonical path alias, malformed manifest and missing driver catalog.
- Native state-path checks reject actual NTFS directory junctions and multiply linked files through the production handle-based validator; fixtures stay in build/ and do not touch ProgramData.
- PE dependency inspection found that upstream CLI and driver require VCRUNTIME140. Packages now include a Microsoft-signed app-local CLI runtime and a hash/version/signature-verified offline x64 runtime installer. Full Setup prepares the driver runtime without scheduling a restart; the runtime installer itself was not executed during this follow-up.
- A live Nominatim search passed in the normal user context. The restricted test sandbox could not acquire Windows TLS credentials; this environment failure was independently reproduced with a standard HTTPS request, without disabling certificate verification.
- One-click prerequisites and hash validation work in Windows PowerShell 5.1 and PowerShell 7; portable packages are tested after extraction to a directory containing Chinese characters and spaces, with the application run outside its source tree.

The audio path uses only embedded PCM and the Windows waveform API; it does not need a temporary audio directory, external player, network download or optional media decoder. Muted playback checks device initialization and completion, not audible loudspeaker output. A missing audio device stops music without preventing normal app use.

**Limits of this follow-up:** no real GNSS/driver success was established (`hardwareVerified=false`). First-time driver setup, BCD changes, UAC, reboot, upgrade/uninstall and every supported Windows version require separate physical/VM validation. The local host has no Inno Setup compiler; the PR's Windows CI builds the installer and checks the extracted portable package. The older validation record below is historical and is not evidence of new hardware validation.

本轮已复测自测、四语 UI、内置原生 PCM、部署完整性和离开源码目录的便携运行。驱动与实际硬件未重新验证；“一键启动”限定于文档列出的 Windows x64 环境，并不自动绕过签名、Secure Boot 或组织策略。

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
