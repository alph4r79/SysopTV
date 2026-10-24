# Setting up WooCommerce for SysopTV

The Windows program can show the orders and revenue of a WooCommerce shop on a cube. For that the shop needs the WordPress plugin "SysopTV for WooCommerce" from this repository. The plugin adds one small endpoint that returns totals only, protected by a key.

## 1. Get the plugin

Download `sysoptv-woocommerce-<version>.zip` from the GitHub Releases of this repository, or build it yourself from the root of the repository:

```sh
python wordpress/tools/build-plugin.py
```

The ZIP lands in `dist/`. Do not unpack it.

## 2. Install it

1. In WordPress open Plugins, Add New, Upload Plugin.
2. Choose the ZIP and press Install Now, then Activate.

WooCommerce must be active, otherwise the plugin only shows a hint. The plugin needs WordPress 6.0, WooCommerce 7.1 and PHP 7.4 or newer. It works with the high performance order storage (HPOS) and with the classic storage.

## 3. Create the key

1. Open WooCommerce, SysopTV. You need the right to manage WooCommerce.
2. Press "Create key" (German: "Schlüssel erzeugen").
3. The key is shown exactly once. Copy it now. The site only stores a hash of it, it cannot show the key again.

"Create new key" makes the old key invalid at once. "Revoke access" removes the key, after that the program cannot read the shop.

The page also shows the address of the endpoint, the time of the last successful request and whether HTTPS is active.

## 4. Enter it in the program

1. Open the page "WooCommerce" in the SysopTV program.
2. Enter the address of the website, for example `https://meinshop.de`, and the key. The key is stored only encrypted for your Windows user.
3. Press "Shop testen". It shows the shop name, today's orders and the version of the plugin, or what went wrong.
4. On the page "Würfel" choose "Shop als Bild" for a cube and "WooCommerce" as its source.

The program asks the shop every 5 minutes. It keeps the last valid numbers in `%LOCALAPPDATA%\SysopTV\state.json` (without the key), so a cube shows them with "Stand HH:MM" right after a restart.

## Which data leaves the shop

`GET /wp-json/sysoptv/v1/stats` with the key in the header `X-SysopTV-Key` returns JSON with:

- `plugin`, `shop`, `currency`, `timezone`, `utcOffsetMinutes`, `generatedAt`
- `today`, `yesterday`, `week` (since Monday 0:00), `month` (since the 1st), `last7Days`: each with `orders` and `revenue`
- `days`: the last 14 days, oldest first, each with `date`, `orders` and `revenue`
- `unshipped`: orders still to ship
- `lastOrder`: `time` and `amount` of the last order, or `null`
- `todayOrders`: today's orders, newest first, at most 20, each with `number` (like `#1043`), `time`, `amount` and `shipped`

Amounts are decimal strings with a point (`"184.50"`), times are in the time zone of the WordPress settings with their offset.

**No customer data**: no names, addresses, e-mail addresses, phone numbers, IP addresses, notes or items.

How the plugin counts:

- Orders with the status processing, completed and on-hold count. Processing also counts as still to ship (`unshipped`). Completed counts as shipped in the list of today.
- Cancelled, refunded, failed, pending, draft (checkout-draft) and trashed orders do not count.
- Revenue is the order total as WooCommerce shows it (`WC_Order::get_total()`): gross, including shipping and taxes, after discounts, refunds not deducted. That is the same definition as for Shopify in the program (`totalPriceSet`).
- Days, weeks and months follow the time zone of the WordPress settings.

## Limits and security

- Only HTTPS. Without HTTPS the endpoint answers 403. Only a site with the environment type `local` (`WP_ENVIRONMENT_TYPE`) may use HTTP, for tests.
- At most one request every 30 seconds, otherwise 429 with `Retry-After`. The program waits that long.
- After 10 wrong keys within 10 minutes the requesting address is locked for 15 minutes (429).
- The totals are cached for 60 seconds. Orders are read in pages of 200, so big shops do not run into the time limit.

## Troubleshooting

- **"Die Website antwortet nur über HTTPS"**: enter the address with `https://`. If the site has no certificate yet, set one up first (most hosts offer Let's Encrypt).
- **"Das Plugin antwortet nicht"** (404): is the plugin installed and active? The program also tries `?rest_route=/sysoptv/v1/stats` for sites without pretty permalinks.
- **"Zugriff verweigert"** (403 without the HTTPS message): a security plugin or a firewall (for example a web application firewall of the host) blocks the REST API for visitors that are not logged in. Allow the route `/wp-json/sysoptv/v1/stats`, or the header `X-SysopTV-Key`.
- **"Die Antwort der Website ist nicht lesbar"**: a caching plugin, a CDN or a maintenance mode serves an HTML page instead of the endpoint. Exclude `/wp-json/` from caching. The plugin already sends `Cache-Control: no-store`.
- **"Zu viele Anfragen"**: the plugin allows one request every 30 seconds. "Shop testen" right after an automatic query has to wait a moment.
- **"Der Schlüssel fehlt oder ist falsch"**: create a new key in WooCommerce, SysopTV and enter it again.

To remove everything the plugin stored, deactivate and delete it in WordPress. `uninstall.php` removes its options and transients. Orders are never touched.
