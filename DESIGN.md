---
version: alpha
name: Sarab Desktop
colors:
  primary: "#68D5AB"
  background: "#151D22"
  surface: "#1D272D"
  foreground: "#EAF1ED"
  muted: "#A6B4AE"
  danger: "#F0A5AE"
  on-primary: "#10221A"
  danger-soft: "#472B32"
  celebration-red: "#690812"
  celebration-gold: "#F0C747"
typography:
  interface:
    fontFamily: "Microsoft YaHei UI, Segoe UI, sans-serif"
    fontSize: "13px"
  noticeHeadline:
    fontFamily: "Microsoft YaHei UI, Segoe UI, sans-serif"
    fontSize: "34px"
  coordinates:
    fontFamily: "Consolas, monospace"
    fontSize: "16px"
rounded:
  card: "18px"
  control: "9px"
  notice: "14px"
omitted:
  - section: spacing
    reason: "WPF spacing remains component-specific; no shared spacing token owner exists."
components:
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.card}"
    padding: "22px"
  button:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.control}"
  caption:
    textColor: "{colors.muted}"
    typography: "{typography.interface}"
  access-denied-teaser:
    textColor: "{colors.celebration-red}"
    rounded: "{rounded.notice}"
    padding: "14px"
  access-denied-headline:
    textColor: "{colors.celebration-gold}"
    typography: "{typography.noticeHeadline}"
  certificate-details:
    backgroundColor: "{colors.celebration-red}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.notice}"
    padding: "14px"
  award-label:
    backgroundColor: "{colors.danger-soft}"
    textColor: "{colors.danger}"
    rounded: "{rounded.control}"
---

# Sarab Desktop design direction

## Product context

- **Register:** Windows desktop control console for a virtual GNSS driver. Users need readable status, deliberate location changes, and clear installation warnings.
- **Audience and scene:** global users at a Windows desktop; Japanese is a supported locale, not evidence of Japan-only business rules. The product is a technical utility, not a consumer map or an installer wizard.
- **Visual reference:** a compact instrument console with a Windows 11 Mica foundation, precise location data, and a restrained signal color. Keep the Sarab identity and use opaque, readable content surfaces with fine edge highlights. Group the sidebar into location work and system tools, with live driver status anchored at the bottom; this navigation organization takes cues from Clash Verge, without copying its source, assets, or screen composition.
- **Material rules:** use native DWM Mica as the long-lived window backdrop where supported, and use a theme-matched solid surface when it is unavailable. Let modest translucency show only in shell surfaces and gaps. Keep cards, forms, and safety information readable; reserve frosted Acrylic for transient surfaces such as menus.
- **Anti-reference:** generic web-dashboard cards, neon gradients, decorative map imagery, ornamental Japanese motifs, and low-contrast glass controls.

## Runtime token ownership

`src/Appearance.cs` is the semantic theme adapter. `MainWindow.xaml` consumes its `DynamicResource` keys; changing a theme or accent must update the existing tree without rebuilding the window. The initial dark values below are the XAML fallback palette; the adapter supplies light/system values at runtime.

| Role | Dark | Light | Runtime key |
|---|---|---|---|
| Window fallback | `#151D22` | `#F2F6F4` | `AppBg` |
| Navigation tint | `#0F171B` at 85% | `#EBF0EC` at 90% | `NavBg` |
| Card | `#1D272D` at 93% | white at 97% | `CardSurface` |
| Raised / alternate | `#222E34` / `#2A383F` | white / `#F5F8F4` | `Elevated` / `SurfaceAlt` |
| Card edge | white at 15% | `#DCE7E1` | `CardEdge` |
| Primary text | `#EAF1ED` | `#17251F` | `Text` |
| Secondary text | `#A6B4AE` | `#5D7067` | `MutedText` |
| Coordinates / secondary emphasis | `#C6D2CC` | `#42574E` | `SoftText` |
| Accent | user choice: mint, ocean, violet, amber | same | `Accent` |
| Danger | `#F0A5AE` | `#A93445` | `Danger` |

## Typography, spacing, and shape

- Use the system UI fonts with Microsoft YaHei UI, Segoe UI, and locale fallbacks. Keep coordinate values in Consolas and use invariant formatting for stored/CLI numeric values.
- Preserve a readable 13px base size and offer 90–125% scaling in five-point steps. The settings window must remain resizable, with scrollable settings rather than clipped content.
- Cards use restrained 18px corners, controls 9–12px corners, and a 1px semantic edge. Keep the sidebar grouped by task with live driver status at the foot; the default width is 220 DIPs, the icon rail is 76 DIPs, and the expanded range tops out at 360 DIPs. At the icon rail, show a fine-edged raised capsule beside each hovered or focused icon so its localized label remains discoverable.
- Use native keyboard focus, visible focus borders, and standard Windows control semantics. Avoid animation that competes with live status.
- Known Windows access-denied errors use a compact certificate-style warning. The teaser and expanded certificate embed the original `good_news.jpg` background from the Good News Generator repository; the teaser crops it into an incomplete ribbon, while the expanded certificate keeps its Chinese heading visible and places the centered gold denial headline directly over the image with a transparent backing and dark shadow. The longer explanation sits on a translucent red panel. Double-click or Enter/Space opens the localized full explanation and starts celebratory music without an on-screen playback announcement. Use a slow, smooth RGB border cycle and gold shimmer only when Windows allows client-area animation; High Contrast keeps static system colors. This playful treatment never applies to generic errors or success notices.

## Locale and copy

- Supported UI locales: automatic Windows preference, Simplified Chinese (`zh-CN`), Traditional Chinese (`zh-TW`), Japanese (`ja-JP`), and English (`en-US`). Japanese copy uses concise, natural UI Japanese with consistent GNSS/driver terminology.
- Keep user-entered place names, provider data, coordinates, and historical log records unchanged by UI locale changes.
- Driver, firmware, Secure Boot, and disk-protection warnings must retain their exact safety meaning in every locale. Do not translate a technical product name or Windows setting into an ambiguous action.
