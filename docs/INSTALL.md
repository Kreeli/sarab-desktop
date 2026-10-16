# Install, verify and remove

[简体中文](INSTALL.zh-CN.md) · [Project home](../README.md)

Sarab's original driver and CLI are by [TheMRVX](https://github.com/TheMRVX/sarab). Sarab Desktop is an independent GUI and installation companion. The pinned v1.0.0 driver is **test-signed**, which is an important limitation of this release.

## 1. Install the controller

Run `SarabDesktop-1.1.0-Setup-x64.exe` and select English or Simplified Chinese. Setup requests administrator approval and installs for all users in the protected `Program Files\SarabDesktop` folder, then creates Start Menu shortcuts and an uninstaller. It can open **Setup & repair** afterwards but does not install a driver, enable test mode or turn on autostart silently. A portable ZIP provides standard-user features only; administrator actions require the installed copy.

No SDK, Rust, Node.js, Python or separate VC++ redistributable is needed. Use Windows 10 1809+ / Windows 11 on an x64 PC. ARM64 is unsupported. The app binaries are not publisher-signed; Windows may show an unknown publisher. Obtain them only from the project's release and compare SHA256SUMS.

## 2. Check system requirements

Open **Setup & repair** and refresh. If the driver is already healthy, leave it installed and proceed to location setup. Otherwise check:

- **Secure Boot:** the test driver requires it off. The app does not modify UEFI. Follow the PC manufacturer's instructions; do not change firmware without having any required disk recovery key.
- **Disk protection:** back up your BitLocker / Device Encryption recovery key before changing Secure Boot or boot settings. The helper will not change boot settings while disk protection is active, encryption/decryption is in progress, or the state cannot be confirmed.
- **Test signing:** the preparation button backs up BCD under `%PROGRAMDATA%\SarabDesktop\Setup`, checks the above conditions with administrator rights, then runs `bcdedit /set {current} testsigning on`. This relaxes driver signature restrictions and may display the Windows Test Mode watermark. A manual restart is required.

On a BitLocker-encrypted system, **full decryption is not required**: after saving the recovery key, suspend protection yourself for the required restart, and confirm it resumes afterwards. In an elevated terminal, an example for the system drive is:

```powershell
manage-bde -status C:
manage-bde -protectors -disable C: -RebootCount 1
```

Use the actual system-drive letter if different. Device encryption, organizational policies or firmware changes may require a different recovery procedure; use your device/organization instructions. The app never suspends protection or stores recovery keys. If it cannot confirm the disk state, it stops; it does not guess. Review the local BCD backup and use Microsoft's documented manual process if your supported system lacks the management interface. Do not disable security controls you are required to keep enabled.

Click **Check and prepare Windows**, approve UAC, then restart at a time you choose. Reopen Setup & repair afterwards; “Test signing: Active in this boot” must be shown before installation. No automatic reboot is scheduled.

## 3. Install the driver

Click **Install / repair original driver** and read the confirmation. UAC elevates only `SarabDesktop.Helper.exe` from the protected install folder. It verifies the pinned CLI, DLL, catalog, INF and certificate, then invokes the original Sarab installer. Administrator actions are blocked when the app runs from a portable or user-writable folder.

This imports the upstream test certificate (thumbprint `40BDA793F3AE6F6A12022D05497D43D9154793EE`) into LocalMachine **Root** and **TrustedPublisher**, and registers `Root\SarabGnss`. The helper checks the PnP hardware identity and ProblemCode=0; successful console output alone is not proof. Setup logs and boot backups are under `%PROGRAMDATA%\SarabDesktop\Setup`.

A healthy existing driver is not reinstalled. A new/repaired device starts with virtual location paused. No sample location is automatically applied. If the device is not healthy, review Device Manager and `%WINDIR%\INF\setupapi.dev.log`. Code 52 commonly indicates a signing issue; other policies, Memory Integrity or a pending restart may affect loading. The app does not turn off Memory Integrity or force a restart.

## 4. Choose and verify a location

Open Windows Settings → Privacy & security → Location. Allow location services and desktop-app access yourself. The app only opens this page; it does not change permissions.

In Overview, choose your own latitude/longitude and select **Apply and verify**, approving UAC. A verified state requires a fresh actual Windows Geolocator reading from the satellite source close to the driver's configured point. Readings expire after 60 seconds; select Verify to sample again. The initial London example is not applied automatically. For an app or browser, also grant that app/site permission and test it separately.

## Upgrade and uninstall

Exit the controller using its tray menu before upgrading. Your `%LOCALAPPDATA%\SarabDesktop` settings, favorites and encrypted key are retained. An installed GUI can be removed from Windows Installed Apps; this **keeps the driver and its current virtual location** and preserves user data.

To remove the driver, first open Setup & repair → **Remove Sarab driver**. This may let Windows use real location sources again. After confirmation and UAC, only devices with the exact Sarab hardware ID and verified corresponding OEM INF packages are removed. Shared test certificates and global test signing are retained because another installation may rely on them.

If you no longer use any test-signed drivers, you may manually disable test mode in an elevated terminal and restart:

```powershell
bcdedit /set {current} testsigning off
```

Do not restore the entire saved BCD blindly, or remove certificates used by another installation. If you have confirmed this certificate is no longer needed, remove **only the exact thumbprint above** from LocalMachine Root and TrustedPublisher using Windows certificate management. Restore Secure Boot according to your PC's instructions, with recovery-key precautions. Never remove unrelated trust roots.

## References

- [Sarab v1.0.0 instructions](https://github.com/TheMRVX/sarab/tree/v1.0.0)
- [Microsoft: test-signing boot option](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option)
- [Microsoft: manage-bde protectors](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/manage-bde-protectors)
