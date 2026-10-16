# Sarab Desktop interaction contract

## Navigation and layout

- The desktop sidebar can be resized between 76 and 360 DIPs and remembers its last width. Widths below 188 DIPs snap to the 76-DIP icon rail when resizing ends; the icon rail hides group and status text while preserving the selected-page marker.
- In the icon rail, hover or keyboard focus opens a theme-aware rounded capsule beside the icon with the active-language page label. Each icon button retains an accessible name in every width and locale.
- The splitter supports dragging and a keyboard alternative: Left / Right adjusts width; Home / End selects the narrowest / widest size. Keep the main page readable at the application's minimum window size.

## Settings

- Language supports Auto, zh-CN, zh-TW, ja-JP, and en-US. Auto follows the current Windows user's preferred UI language and responds to a Windows locale change while the app is open. Manual selection persists across restarts.
- A language change updates every application-owned control, page title, dialog/notice generated after the change, tray label/menu, and installation guide choice. It does not rewrite user place names or old log entries.
- Theme supports Windows-following, light, and dark. Accent and font scale apply immediately and persist. Font scaling is clamped to 90–125% in 5% increments.
- The app uses the native Windows system backdrop when DWM supports it and falls back to an opaque theme surface otherwise. High Contrast uses Windows system colors and disables the backdrop. Appearance changes update the backdrop/title bar together, while cards and safety instructions retain readable contrast.
- The settings window remains scrollable and controls keep their accessible labels and keyboard behavior in all supported locales.

## Location and driver safety

- Saving a target or favorite is distinct from applying it. Applying still requires its existing explicit action and administrator prompt; verification describes the value returned by Windows rather than inferring success from configuration.
- Driver preparation never silently disables Secure Boot, suspends disk protection, or restarts the computer. Memory Integrity/HVCI is displayed as diagnostic context and is never disabled by the app.
- Search-provider privacy text must match the selected provider. Search failures never silently switch providers.
- Imported and persisted favorites are validated, limited to 200 entries, and never include the protected Google API key.
- Undoing favorite removal must respect the same limit and must not create duplicate records.

## Runtime feedback

- Known Windows access-denied failures are shown in the active UI language; the original diagnostic remains in the local operation log.
- A known access-denied failure uses a red/gold certificate teaser with a horizontally centered localized denial headline and an incomplete one-line summary. Double-click or Enter/Space opens the full localized explanation; Escape and the close button dismiss it. Generic warnings and success notices keep the standard notice style. RGB frame and gold shimmer animations are slow and stop under High Contrast or when Windows disables client-area animation.
- Restoring the main window from the tray invalidates stale probe evidence and immediately refreshes status asynchronously.
- A second launch with `--setup` activates the running instance and opens the setup page. A normal second launch only activates the current window.
- Theme changes update semantic resource tokens across the current control tree. New UI text must be registered in all four translation resources and covered by the localization check.
