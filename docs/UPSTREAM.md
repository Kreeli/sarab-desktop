# Relationship with Sarab / 与上游的关系

## English

This is a **companion repository**, not a fork of the complete upstream project. It contains our independent WPF GUI and supporting installation/build code. Dependencies are downloaded from the upstream's fixed v1.0.0 release and verified against `dependencies.lock.json`; upstream driver/CLI code is not rewritten or attributed to us.

This structure fits the current contribution: we maintain the desktop interface while TheMRVX remains clearly credited for the core. A GitHub fork would be appropriate if we begin modifying the upstream driver, Rust CLI or original web dashboard: fork the upstream, retain its history, license and NOTICE, use a feature branch, and submit changes by pull request if appropriate. Do not copy the latest upstream source into this repository under our GUI's MIT license.

The pinned release's original MIT license is in `third_party/sarab/LICENSE` and shipped as `backend/Sarab-LICENSE.txt`. The upstream main branch now uses Apache-2.0. Upgrading that dependency requires a fresh review of its version, hashes, license, NOTICE and compatibility. This project does not claim an official partnership or endorsement.

## 简体中文

当前采用**独立配套仓库**，保存我们独立实现的 WPF GUI、安装引导及构建代码。构建时从原项目固定的 v1.0.0 Release 下载依赖并校验，不把上游驱动或 CLI 的创作归到自己名下。

这符合当前实际贡献：我们维护桌面界面，TheMRVX 负责原定位核心。以后若修改驱动、Rust CLI 或原网页控制台，应 fork 上游，保留提交历史、许可证与 NOTICE，另开功能分支，并视情况向原作者提交 PR。不要把上游最新源码复制过来后统一套用 GUI 的 MIT 许可。

固定旧版的原始 MIT 许可证保存在 `third_party/sarab/LICENSE`，安装包也保留副本。当前上游 main 已使用 Apache-2.0；将来升级依赖要重新检查版本、哈希、许可、NOTICE 和兼容性。独立仓库不表示官方合作或原作者背书。
