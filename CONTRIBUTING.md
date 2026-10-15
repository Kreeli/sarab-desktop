# Contributing / 参与开发

English | 简体中文

Keep changes focused on the desktop controller. Credit [TheMRVX / Sarab](https://github.com/TheMRVX/sarab) for the driver and CLI. Read [UPSTREAM](docs/UPSTREAM.md) before changing dependencies or considering a fork. See the [English](README.md) and [Chinese](README.zh-CN.md) build instructions.

- Add UI strings to `locales/en-US.json`; the Chinese source string is the stable lookup key. Use `L.T` / `L.F` in C# and static text in XAML. Keep user-entered place names and old logs unchanged.
- Run `--self-test` after a build. Use isolated `--render` and `--ui-smoke` diagnostics to inspect both languages. Test actual driver changes only on your own prepared machine or a dedicated VM; CI never changes boot settings or installs drivers.
- Keep new installs paused. Do not add silent changes to firmware, BitLocker, location permissions or global signing policy.
- Never commit personal settings, keys, logs, screenshots of private coordinates, build artifacts or upstream binary caches. CI artifacts must contain only `dist` and release documentation.
- Preserve both READMEs, NOTICE, original licenses and the pinned dependency hashes. Document actual validation and untested conditions; a successful configuration write alone is not proof that all apps use the target.

请将改动聚焦于桌面控制器。驱动与 CLI 的原作者为 TheMRVX；依赖升级或 fork 前先阅读上游关系说明。新界面文案须补齐中英翻译；用户地点名、历史日志不做翻译。构建后运行基础检查和隔离界面诊断，真实驱动变更仅在自己准备好的电脑或测试虚拟机上操作。

新安装默认暂停，不添加静默修改固件、BitLocker、位置权限或全局签名策略的逻辑。不提交私人配置、Key、日志、含私人坐标的截图、构建产物或上游二进制缓存。保留双语文档、原作者署名、许可证和固定哈希；如实记录验证范围。
