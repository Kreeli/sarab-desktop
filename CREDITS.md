# Credits / 贡献与署名

English · [简体中文](#简体中文)

**The location engine belongs to the original Sarab project by [TheMRVX](https://github.com/TheMRVX/sarab).**

TheMRVX created the virtual GNSS driver, core Windows location integration, Rust CLI and original web dashboard. This application bundles the **unmodified Sarab v1.0.0** release, pinned to source commit `7b0b40cba88b78196858303284a2d1f576903b14`. Its original MIT copyright and license are retained.

[Kreeli](https://github.com/Kreeli) and Sarab Desktop contributors maintain this independent Windows desktop companion: its WPF GUI, English/Chinese language support, location profiles, verification display, packaging and installation guidance. We do not claim to have created Sarab's location engine. This is not an official release of Sarab and does not imply endorsement by TheMRVX.

The visual design is inspired by [FlClash](https://github.com/chen08209/FlClash). The grouped appearance controls also take interaction cues from [Token-Monitor](https://github.com/Lazyzouo/Token-Monitor). No code, icons or fonts from either project are redistributed. Address results from OpenStreetMap / Nominatim are attributed in the UI; optional Google Geocoding uses the user's own key.

The certificate's embedded retro music is based on 20 loops by **Snabisch**, from [20 excellent 8 bit loops](https://opengameart.org/content/20-excellent-8-bit-loops), licensed under [CC-BY 3.0](https://creativecommons.org/licenses/by/3.0/). The tracks were converted from stereo 44.1 kHz / 16-bit PCM to mono 11.025 kHz / 8-bit PCM for this application.

The expanded certificate uses the original `src/assets/good_news.jpg` background from [vonbrank/good-news-generator](https://github.com/vonbrank/good-news-generator), embedded unchanged at the user's request. The repository does not declare a license for this asset, so permission for onward redistribution has not been verified.

## 简体中文

**定位核心来自 [TheMRVX 的 Sarab 原项目](https://github.com/TheMRVX/sarab)。**

TheMRVX 完成了虚拟 GNSS 驱动、Windows 定位接入、Rust CLI 和原网页控制台。本项目分发未经修改的 **Sarab v1.0.0**，固定源码提交为 `7b0b40cba88b78196858303284a2d1f576903b14`，保留该版本原有 MIT 版权及完整许可证。

[Kreeli](https://github.com/Kreeli) 与 Sarab Desktop 贡献者负责独立的 WPF 桌面 GUI、中英日繁四语支持、位置配置管理、定位核验展示、外观设置、打包与安装引导。我们没有重写或声称创造定位核心，也不代表原作者官方发布或获得其背书。

界面视觉参考 FlClash；分组外观设置的交互参考 Token-Monitor。未复制或分发这两个项目的源码、图标或字体。免 Key 地址搜索由 OpenStreetMap / Nominatim 提供，界面显示来源；可选 Google Geocoding 使用用户自己的 Key。

喜报证书内置的复古音乐改编自 **Snabisch** 的 [20 excellent 8 bit loops](https://opengameart.org/content/20-excellent-8-bit-loops)，采用 [CC-BY 3.0](https://creativecommons.org/licenses/by/3.0/) 许可。为适配应用体积与复古音色，音轨从立体声 44.1 kHz / 16-bit PCM 转换为单声道 11.025 kHz / 8-bit PCM。

展开喜报使用 [vonbrank/good-news-generator](https://github.com/vonbrank/good-news-generator) 仓库中的原始 `src/assets/good_news.jpg` 背景图，按用户要求原图嵌入。该仓库没有为此素材声明许可证，因此后续对外再分发授权尚未核实。

上游当前主分支的 Apache-2.0 许可不被套用到固定的旧版本；将来升级上游时需要重新核对许可证及 NOTICE。
