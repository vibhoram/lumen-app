**Lumen {VERSION}**, a Webser studio Dynamic Island for Windows 10 and 11. Made by Vibhor Maheshwari · [webser.app](https://webser.app)

Based on D-Island by h7 (ibuildrigs) — https://github.com/H7gamer/d-island

### What's new in 1.3.2
- **Settings:** one custom title bar, nav icons, smoother scroll, Clara fields that no longer overlap their labels.
- **Terms & privacy:** TERMS.txt in the install; About opens the legal files; PRIVACY matches real update behaviour.
- **Defaults:** fresh installs no longer ship a maintainer bio into Clara; Groq keys can be cleared.

### What's new in 1.3.1
- **Webser:** studio mark and webser.app in About (vector W, not a pasted tile).
- **Public releases:** installers and in-app updates ship from this public repo. Clara needs your own API key (Gemini preferred).

### What's new in 1.3.0
- **Matching glass bubbles:** weather and carry bubbles share the same live Liquid Glass as the pill.
- **Clara · Gemini first:** Gemini is the main cloud brain; Groq only steps in as a failsafe. Roomier chat and a portrait beside replies.
- **Snappier Settings:** Settings stays warm so opening it from the island doesn't hitch the collapse.
- **Cleaner tool replies:** Gemini's fake tool XML spam is stripped; real tools still run.

### Highlights
- **Pages:** Home, Clara, Clipboard, Focus, Notifications, Calendar, To-Do, Notes, Shelf, Weather, Apps and more.
- **Alerts:** volume, brightness, battery, devices, mic/camera, Caps Lock, copy/paste, Do Not Disturb, Wi-Fi/VPN, calendar reminders.
- **Looks:** island or notch, Liquid Glass, music bars that follow the sound.
- **Updates itself:** Lumen checks this repo once a day and installs when the island is idle (Settings > Updates).

### Install
Download **Lumen-Setup-{VERSION}.exe** below and run it. Per-user install (no admin). A short guide is shown during setup.

**"Windows protected your PC"?** Lumen isn't code-signed yet, so SmartScreen may warn once. Check the hash below, then **More info** → **Run anyway**. Updates after that install without the prompt.

### Verify your download
SHA-256 of `Lumen-Setup-{VERSION}.exe`:
```
{SHA256}
```
Check it in PowerShell: `Get-FileHash .\Lumen-Setup-{VERSION}.exe`

### Privacy
No accounts, analytics or tracking. See PRIVACY.txt. Clara only talks to Gemini/Groq when you send a message, with your own key.

Webser · [webser.app](https://webser.app) · [github.com/vibhoram/lumen-app](https://github.com/vibhoram/lumen-app) · D-Island License 1.0
