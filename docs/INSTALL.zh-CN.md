# 安装、核验与卸载

[English](INSTALL.md) · [项目首页](../README.zh-CN.md)

驱动和 CLI 来自 [TheMRVX 的 Sarab 原项目](https://github.com/TheMRVX/sarab)。Sarab Desktop 负责独立 GUI 和安装引导。固定的 v1.0.0 驱动仍使用**测试签名**，这是当前分发方式的重要限制。

## 1. 安装上位机

运行 `SarabDesktop-1.1.0-Setup-x64.exe` 并选择中英文。安装程序会请求管理员确认，将应用为所有用户安装到受保护的 `Program Files\SarabDesktop` 目录，并创建开始菜单入口与卸载器；之后可打开“安装与修复”。它不会静默安装驱动、开启测试模式或开启自启动。便携 ZIP 仅支持普通权限功能；管理员操作必须使用已安装版本。

朋友不需要 SDK、Rust、Node.js、Python 或单独安装 VC++ 运行库。支持 Windows 10 1809+ / Windows 11 的 x64 电脑，不支持 ARM64。新增程序尚无发布者代码签名，Windows 可能显示未知发布者；请仅从本项目 Release 获取并核对 SHA256SUMS。

## 2. 检查系统条件

进入“安装与修复”并刷新。如果驱动已正常工作，保持现有安装，直接设置位置。否则检查：

- **Secure Boot**：测试驱动要求关闭它。程序不会修改 UEFI。按电脑厂商的说明操作，先确认持有所需的磁盘恢复密钥。
- **磁盘保护**：更改 Secure Boot 或启动配置前，先备份 BitLocker / 设备加密恢复密钥。保护仍开启、磁盘正在加解密或状态无法确认时，辅助程序会停止修改启动项。
- **测试签名**：准备按钮以管理员身份检查条件，先备份 BCD 到 `%PROGRAMDATA%\SarabDesktop\Setup`，然后执行 `bcdedit /set {current} testsigning on`。这会放宽驱动签名限制，可能显示 Windows 测试模式水印，需要手动重启。

已经加密的磁盘**不需要完整解密**。备份恢复密钥后，按设备说明手动暂停保护以完成所需重启，并在启动后确认保护恢复。管理员终端中针对系统盘的示例：

```powershell
manage-bde -status C:
manage-bde -protectors -disable C: -RebootCount 1
```

若系统盘不是 C 盘，请使用实际盘符。设备加密、组织策略或固件修改可能需要不同的恢复流程，请遵循设备/组织说明。程序不会自动暂停保护，也不保存恢复密钥。磁盘管理接口不可用时会停止，不会猜测状态；如确认系统支持且具备恢复准备，可对照本地备份和微软文档使用手动流程。不要关闭组织要求保留的安全控制。

点击“检查并准备 Windows”，同意 UAC，之后自行选择时间重启。重开向导，确认“测试签名：本次启动已生效”。程序不会安排自动重启。

## 3. 安装原版驱动

点击“安装 / 修复原版驱动”，阅读确认说明。只有受保护安装目录中的 `SarabDesktop.Helper.exe` 请求管理员权限。辅助程序校验固定 CLI、DLL、CAT、INF 和证书后，调用原 Sarab 安装器。从便携目录或用户可写目录启动时，程序会阻止管理员操作。

此操作将测试证书（指纹 `40BDA793F3AE6F6A12022D05497D43D9154793EE`）导入 LocalMachine 的 **Root** 与 **TrustedPublisher**，并注册 `Root\SarabGnss`。安装后核对硬件标识和 ProblemCode=0，不把控制台成功文字当作安装证明。日志及启动备份在 `%PROGRAMDATA%\SarabDesktop\Setup`。

已有健康驱动不会被重装。新安装或修复后默认暂停虚拟定位，不会自动应用示例位置。设备异常时查看设备管理器和 `%WINDIR%\INF\setupapi.dev.log`；代码 52 常见于签名条件不满足，其他策略、内存完整性或待重启也可能影响加载。程序不会关闭内存完整性或强制重启。

## 4. 设置并核验位置

在 Windows 设置 → 隐私和安全性 → 位置，手动允许位置服务与桌面应用访问。上位机只打开该页面，不代替你更改权限。

在概览填写自己的经纬度，点击“应用并核验”并同意 UAC。核验通过需要独立 Windows Geolocator 返回新采样、来源为 Satellite、坐标接近驱动配置。采样 60 秒后显示过期，可以再次核验。初始伦敦示例不会自动应用。浏览器或软件还需自行授权并单独验证。

## 升级与卸载

升级前从托盘菜单退出上位机。`%LOCALAPPDATA%\SarabDesktop` 内的设置、收藏和加密 Key 会保留。Windows“已安装的应用”可卸载 GUI，**不会移除驱动或取消当前虚拟位置**，也不会清除本机数据。

需要移除驱动时，先进入“安装与修复”→“卸载 Sarab 驱动”。之后 Windows 可能恢复真实位置来源。经明确确认与 UAC，仅移除硬件 ID 精确匹配 Sarab 的设备和核实属于 Sarab 的 OEM INF 包。共享测试证书和全局测试模式保留，因为其他安装可能依赖它们。

确定不再使用任何测试签名驱动后，可在管理员终端手动关闭测试模式，再自行重启：

```powershell
bcdedit /set {current} testsigning off
```

不要盲目整体恢复旧 BCD，也不要删除其他安装使用的证书。确认上述证书不再被使用后，可用 Windows 证书管理工具从 LocalMachine Root 和 TrustedPublisher 中仅移除该**精确指纹**。恢复 Secure Boot 时遵循电脑厂商说明并做好恢复密钥准备，不要删除无关根证书。

## 参考

- [原版 Sarab v1.0.0 说明](https://github.com/TheMRVX/sarab/tree/v1.0.0)
- [微软：测试签名启动选项](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option)
- [微软：manage-bde protectors](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/manage-bde-protectors)
