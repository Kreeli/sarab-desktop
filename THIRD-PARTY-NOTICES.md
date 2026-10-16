# Third-party notices / 第三方声明

## Sarab — TheMRVX

**The virtual GNSS driver, core location functionality, Rust CLI and original web UI were created by TheMRVX.** Sarab Desktop is an independent GUI and setup companion, not an official Sarab release.

The bundled `backend/sarab.exe` and `driver/SarabGnss.*` are **unmodified v1.0.0 release files**, corresponding to commit `7b0b40cba88b78196858303284a2d1f576903b14`:

https://github.com/TheMRVX/sarab/tree/v1.0.0

MIT License, Copyright (c) 2026 TheMRVX. The complete original license is retained in `third_party/sarab/LICENSE` in this repository and `backend/Sarab-LICENSE.txt` in distributed packages. Fixed release hashes are in `dependencies.lock.json`. The newer upstream main branch uses Apache-2.0 and is not included in this release.

## Certificate music — Snabisch

The 20 embedded retro music loops in `SarabDesktop.exe` are adapted from Snabisch, [20 excellent 8 bit loops](https://opengameart.org/content/20-excellent-8-bit-loops), licensed under [Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/). The audio was converted from stereo 44.1 kHz / 16-bit PCM to mono 11.025 kHz / 8-bit PCM. Track-name mapping is in `assets/notice-music/README.md` in the source repository.

喜报音乐改编自 Snabisch 的《20 excellent 8 bit loops》，采用知识共享署名 3.0（CC-BY 3.0）许可。音频已从立体声 44.1 kHz / 16-bit PCM 转换为单声道 11.025 kHz / 8-bit PCM。原曲名与内置文件的对应关系见源码仓库的 `assets/notice-music/README.md`。

## Certificate background image — Good News Generator

The expanded certificate embeds the original `src/assets/good_news.jpg` from [vonbrank/good-news-generator](https://github.com/vonbrank/good-news-generator), unchanged, at the user's request. The upstream repository does not declare a license for this image; permission for onward redistribution has not been verified.

展开喜报按用户要求原样嵌入 [vonbrank/good-news-generator](https://github.com/vonbrank/good-news-generator) 仓库的 `src/assets/good_news.jpg`。上游仓库没有为该图片声明许可证，对外再分发授权尚未核实。

定位驱动、CLI 和原网页控制台由 TheMRVX 开发。我们仅维护独立桌面 GUI、语言、配置与安装引导，不声称原定位核心的创作权，不暗示原作者背书。固定旧版本保留其 MIT 许可，不混用新版 main 的 Apache-2.0 声明。

## Independent location reader

`backend/location-probe.exe` is built from this repository's `src/location-probe.cpp`, using Windows SDK C++/WinRT Geolocator APIs and static C++ runtime linkage. The newly written source is covered by this project's MIT license. The build embeds the exact generated binary hash into the controller; each distribution also includes `SHA256SUMS.txt`.

## UI design and fonts

The sidebar/card visual design is inspired by [FlClash](https://github.com/chen08209/FlClash). Grouped appearance-setting interaction also takes cues from [Token-Monitor](https://github.com/Lazyzouo/Token-Monitor). No code or graphics from either project are bundled; the pin icon is independently drawn. Microsoft YaHei UI, Segoe UI and Segoe Fluent Icons are referenced as installed system fonts; no font files are redistributed.

## Installer

Installers are produced using [Inno Setup](https://jrsoftware.org/), © Jordan Russell and Martijn Laan. The generated installer retains Inno Setup's own notices. Its compiler is not bundled with this application. The upstream license text is retained in `third_party/inno-setup/LICENSE`.

`installer/ChineseSimplified.isl` retains the original header and translation attribution to Zhenghan Yang (Kira), obtained from Inno Setup's official source tree on 2026-10-16. SHA256: `e0b0b350e2245f3c5e65586dfe43d574f6e7f06f2261149aba284954b3fc9a8d`.

## Address services

OpenStreetMap data: © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright), ODbL. Manual lookup uses Nominatim with UI attribution, request throttling and an in-memory cache, following its [usage policy](https://operations.osmfoundation.org/policies/nominatim/).

Google Maps links and optional Google Geocoding use the user's own network and API credentials under Google's applicable terms. No map tiles, Google scripts, logos or API keys are bundled. Provider choice is explicit and there is no silent search fallback.
