# Privacy and security

After reading this you know which data SysopTV reads, where it goes, what is stored on your PC and what never happens.

You need nothing for this page. It describes the firmware 0.5.0, the Windows program 0.8.0 and the plugin "SysopTV for WooCommerce" 0.2.0.

## Which data goes where

| From | To | What |
|---|---|---|
| Your PC (program) | `api.anthropic.com` | the login of Claude Code, to read your usage limits |
| Anthropic | your PC | session and week in percent with their reset times |
| Your PC | your Shopify shop | client ID and secret (for a token), then queries for orders |
| Shopify | your PC | per order only number, time, amount and whether it is shipped |
| Your PC | your WooCommerce site | the key of the plugin |
| WooCommerce plugin | your PC | totals and today's orders with number, time, amount and shipped or not |
| Your PC | your cubes, in your local network | pictures, NO CARRIER when the program ends, the settings of the cube and a firmware update you start yourself |
| Your PC | `api.github.com` and `github.com` | only if "Einmal täglich nach neuen Versionen suchen" is on (at most once a day) or after a click on "Jetzt nach Updates suchen": the question for the latest release; files of the release only after "Jetzt aktualisieren" or "Firmware aktualisieren" |

The program connects only to `api.anthropic.com`, to your shop at Shopify or your WooCommerce site (only if set up), to your cubes and, only if you switch the update check on, to `api.github.com`. The search for cubes only asks addresses in your local network. There is no telemetry, no account and no cloud. The cube gets only pictures, no numbers and no login.

No customer data is read: no names, addresses, e-mail addresses, phone numbers, IP addresses, notes or items.

## What is stored on your PC

- `%APPDATA%\SysopTV\config.json`: your cubes and settings. API keys of the cubes, admin passwords of the cubes, the Shopify access and the WooCommerce key are stored only encrypted with the Windows data protection (DPAPI) for your Windows user. Another Windows user, or another PC, cannot read them.
- `%APPDATA%\SysopTV\config.bak.json`: the previous `config.json`, with the same encryption. `config.broken.json` exists only if a damaged `config.json` was replaced at the start.
- `%LOCALAPPDATA%\SysopTV\state.json`: time of the last query, a pause after error 429, the last valid Claude values, the last valid shop numbers (no customer data), for the Shopify year orders and revenue per day, which limit messages were shown, and the time and result of the last update check. Never the login of Claude Code, never a key.
- `%LOCALAPPDATA%\SysopTV\app.log`: what the program did, for example "Daten: ok, ..." or errors. At most 1 MB, then one backup `app.old.log`. It contains no login, no key and no password.

The login of Claude Code stays where Claude Code keeps it, in `%USERPROFILE%\.claude\.credentials.json`. The program only reads it.

## What never happens

- The login of Claude Code is never sent to the cube, never stored by the program and never renewed by the program.
- Keys, secrets and passwords are never written to the log or shown in plain text in the window.
- The program never writes to your shop. The Shopify app only needs read access (`read_orders`, `read_all_orders`), the plugin only reads orders.
- The program never installs anything by itself. Files from GitHub are only downloaded after a click on "Jetzt aktualisieren" or "Firmware aktualisieren", checked against `SHA256SUMS.txt`, and used only after you confirm.

## On the cube

- The cube stores only a hash of the admin password (with salt), never the password itself.
- The overview page and `/api/info` never show the password, its hash, the API key or the WLAN password.
- Every form of the web interface is protected against requests from other web pages.

## Admin password of the cube

With an admin password, the settings, the update page, the WLAN setup and the flash backup need a login. Without one, anyone in your network can change them. Set it right after the installation, see [Installation](installation.md#4-set-the-admin-password). The program needs it to load and save the settings in the cube and for the firmware update; it keeps it encrypted.

## API key of the cube

Without an API key, anyone in your network can send pictures to the cube or switch it to NO CARRIER. With a key, only a program that knows the key may do that (`/api/frame` and `/api/nocarrier`).

1. Open "Einstellungen" (settings) in the web interface of the cube and press "Neuen Schlüssel erzeugen" (create new key).
2. Copy the key into the field "API-Schlüssel (optional)" on the page "Würfel" of the program.

If the key is missing or wrong, the cube answers with error 401 and the program shows the cube as not reachable. "Schlüssel löschen" (delete key) switches the protection off again.

## The keys of the shops

- The Shopify client secret belongs only into your own program. The client ID is not a secret.
- The WooCommerce key is shown only once in WordPress. The site stores only a hash of it. "Revoke access" in WordPress makes it useless at once.
- The plugin answers only over HTTPS, at most once every 30 seconds, and locks an address after 10 wrong keys for 15 minutes, see [WooCommerce](woocommerce-setup.md#limits-and-security).

## Update check

"Einmal täglich nach neuen Versionen suchen" (look for new versions once a day) on the page "Programm" is off by default. The program connects to GitHub only while it is on, or once after a click on "Jetzt nach Updates suchen".

- The program asks `https://api.github.com/repos/alph4r79/SysopTV/releases/latest`, without login. Nothing is sent except this normal request: no data about you, your settings or your cubes. GitHub sees your IP address and the program version in the User-Agent, like with any visit to a web page.
- It reads the versions of program and firmware from the file names of the release. Each new version is reported once in the taskbar.
- Files of the release (`sysoptv-app-X.Y.Z-setup.exe`, `sysoptv-X.Y.Z.bin`, `SHA256SUMS.txt`) are only downloaded after a click on "Jetzt aktualisieren" or "Firmware aktualisieren". Every file is checked against its SHA-256 checksum; with a wrong checksum or without `SHA256SUMS.txt` nothing is used.
- Without network it stays silent and tries again an hour later.
