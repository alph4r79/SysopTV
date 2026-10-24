# Privacy and security

After reading this you know which data SysopTV reads, where it goes, what is stored on your PC and what never happens.

You need nothing for this page. It describes the firmware 0.4.3, the Windows program 0.7.0 and the plugin "SysopTV for WooCommerce" 0.2.0.

## Which data goes where

| From | To | What |
|---|---|---|
| Your PC (program) | `api.anthropic.com` | the login of Claude Code, to read your usage limits |
| Anthropic | your PC | session and week in percent with their reset times |
| Your PC | your Shopify shop | client ID and secret (for a token), then queries for orders |
| Shopify | your PC | per order only number, time, amount and whether it is shipped |
| Your PC | your WooCommerce site | the key of the plugin |
| WooCommerce plugin | your PC | totals and today's orders with number, time, amount and shipped or not |
| Your PC | your cubes, in your local network | pictures, the Claude percentages with reset times, and the settings of the cube |

The program connects only to `api.anthropic.com`, to your shop at Shopify or your WooCommerce site (only if set up) and to your cubes. The search for cubes only asks addresses in your local network. There is no telemetry, no account, no cloud and no update check.

No customer data is read: no names, addresses, e-mail addresses, phone numbers, IP addresses, notes or items.

## What is stored on your PC

- `%APPDATA%\SysopTV\config.json`: your cubes and settings. API keys of the cubes, admin passwords of the cubes, the Shopify access and the WooCommerce key are stored only encrypted with the Windows data protection (DPAPI) for your Windows user. Another Windows user, or another PC, cannot read them.
- `%LOCALAPPDATA%\SysopTV\state.json`: time of the last query, a pause after error 429, the last valid Claude values, the last valid shop numbers (no customer data) and, for the Shopify year, orders and revenue per day. Never the login of Claude Code, never a key.
- `%LOCALAPPDATA%\SysopTV\app.log`: what the program did, for example "Daten: ok, ..." or errors. At most 1 MB, then one backup `app.old.log`. It contains no login, no key and no password.

The login of Claude Code stays where Claude Code keeps it, in `%USERPROFILE%\.claude\.credentials.json`. The program only reads it.

## What never happens

- The login of Claude Code is never sent to the cube, never stored by the program and never renewed by the program.
- Keys, secrets and passwords are never written to the log or shown in plain text in the window.
- The program never writes to your shop. The Shopify app only needs read access (`read_orders`, `read_all_orders`), the plugin only reads orders.

## On the cube

- The cube stores only a hash of the admin password (with salt), never the password itself.
- The overview page and `/api/info` never show the password, its hash, the API key or the WLAN password.
- Every form of the web interface is protected against requests from other web pages.

## Admin password of the cube

With an admin password, the settings, the update page, the WLAN setup and the flash backup need a login. Without one, anyone in your network can change them. Set it right after the installation, see [Installation](installation.md#4-set-the-admin-password). The program needs it only to load and save the settings in the cube; it keeps it encrypted.

## API key of the cube

Without an API key, anyone in your network can send numbers to the cube. With a key, only a sender that knows the key may do that.

1. Open "Einstellungen" (settings) in the web interface of the cube and press "Neuen Schlüssel erzeugen" (create new key).
2. Copy the key into the field "API-Schlüssel (optional)" on the page "Würfel" of the program.

If the key is missing or wrong, the cube answers with error 401 and the program shows the cube as not reachable. "Schlüssel löschen" (delete key) switches the protection off again.

## The keys of the shops

- The Shopify client secret belongs only into your own program. The client ID is not a secret.
- The WooCommerce key is shown only once in WordPress. The site stores only a hash of it. "Revoke access" in WordPress makes it useless at once.
- The plugin answers only over HTTPS, at most once every 30 seconds, and locks an address after 10 wrong keys for 15 minutes, see [WooCommerce](woocommerce-setup.md#limits-and-security).
