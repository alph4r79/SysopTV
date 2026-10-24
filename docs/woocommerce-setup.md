# WooCommerce

At the end of this guide a cube shows the orders and revenue of your WooCommerce shop, including the whole year.

You need:

- a WordPress site with WooCommerce, reachable over HTTPS
- the right to install plugins and to manage WooCommerce on that site
- WordPress 6.0, WooCommerce 7.1 and PHP 7.4 or newer
- the Windows program and a cube with firmware 0.4.0 or newer, see [Windows app](windows-app.md)

## How it works

The shop needs the WordPress plugin "SysopTV for WooCommerce" from this repository. It adds one small endpoint, `GET /wp-json/sysoptv/v1/stats`, that returns totals and today's order list, protected by a key. The program asks it every 5 minutes. The plugin reads orders only through the WooCommerce interface and works with the high performance order storage (HPOS) and with the classic storage.

## 1. Download the plugin

Download `sysoptv-woocommerce-<version>.zip` from the [GitHub Releases](https://github.com/alph4r79/SysopTV/releases). Do not unpack it. You can also build it yourself, see [Developers](developers.md#wordpress-plugin).

## 2. Upload and activate it

Source: [WordPress: Manage Plugins, Upload via WordPress Admin](https://wordpress.org/documentation/article/manage-plugins/#upload-via-wordpress-admin)

1. In the WordPress admin go to **Plugins > Add New**.
2. Click **Upload Plugin** at the top of the screen.
3. Choose the ZIP file.
4. Click **Install Now**.
5. Click **Activate Plugin**.

WooCommerce must be active, otherwise the plugin only shows a hint.

## 3. Create the key

1. In the WordPress admin open **WooCommerce > SysopTV**.
2. Press **Create key** (German: "Schlüssel erzeugen").
3. The key is shown exactly once. Copy it now. The site stores only a hash of it and cannot show the key again.

**Create new key** ("Neuen Schlüssel erzeugen") makes the old key invalid at once. **Revoke access** ("Zugang sperren") removes the key; after that the program cannot read the shop.

The page also shows the address of the endpoint, the time of the last successful request and whether HTTPS is active.

## 4. Enter it in the program

1. Open the window of SysopTV and the page **WooCommerce**.
2. **Adresse der Website** (address of the website): the address of the site, for example `https://shop.example.com`. The address of the website is enough, not the address of the endpoint.
3. **Schlüssel** (key): the key from step 3. The program stores it only encrypted for your Windows user.
4. **Name für die Anzeige** (name for the display): the name the cube shows on top, for example "My Shop". Empty means the name of the site.
5. Press **Shop testen** (test the shop). The line next to it shows the shop name, today's orders and the version of the plugin, or what went wrong.
6. On the page **Würfel** choose the function **WooCommerce** for a cube, then the view and the style, see [Windows app](windows-app.md#verbindung-und-funktion-connection-and-function).

## Updating the plugin

1. Download the new ZIP.
2. Upload it the same way as in [step 2](#2-upload-and-activate-it). WordPress then offers to replace the installed version; confirm it.
3. The key stays, you do not have to enter it again.

Do not delete the plugin to update it. Deleting it removes its stored data, including the key.

If the program shows "Plugin aktualisieren" (update the plugin) for the year, the installed plugin is older than 0.2.0. Today, week and month keep working until then.

## What the plugin counts

- Orders with the status processing, completed and on-hold count. Processing also counts as still to ship. Completed counts as shipped in the list of today.
- Cancelled, refunded, failed, pending, draft (checkout-draft) and trashed orders do not count.
- Revenue is the order total as WooCommerce shows it: gross, including shipping and taxes, after discounts, refunds not deducted. That is the same definition as for Shopify.
- Days, weeks, months and the year follow the time zone of the WordPress settings. The week starts on Monday at 0:00.

## The year

The plugin counts orders and revenue since 1 January. So that a request never has to read all orders of the year, it keeps the number of orders and the revenue per finished day (option `sysoptv_wc_days`). Each computation counts only today and the last 30 days again. Once a day it counts all days of the year again, so changes to older orders, for example a cancellation, are picked up the next day. That count runs in steps of at most 5 pages of 200 orders per request.

Right after installing, and on 1 January, the first count is not through yet. The program then shows "Jahr wird noch berechnet" (year is still being counted) for the year. Each request of the program, every 5 minutes, counts up to 1000 more orders, so with a few thousand orders in the year it takes a few rounds.

## Which data leaves the shop

The endpoint returns JSON with:

- `plugin`, `shop`, `currency`, `timezone`, `utcOffsetMinutes`, `generatedAt`
- `today`, `yesterday`, `week` (since Monday 0:00), `month` (since the 1st), `year` (since 1 January), `last7Days`: each with `orders` and `revenue`
- `yearComplete`: `false` while the first count of the year is still running, then `true`
- `days`: the last 14 days, oldest first, each with `date`, `orders` and `revenue`
- `unshipped`: orders still to ship
- `lastOrder`: `time` and `amount` of the last order, or `null`
- `todayOrders`: today's orders, newest first, at most 20, each with `number` (like `#1043`), `time`, `amount` and `shipped`

Amounts are decimal strings with a point (`"184.50"`), times are in the time zone of the WordPress settings with their offset.

**No customer data**: no names, addresses, e-mail addresses, phone numbers, IP addresses, notes or items.

## Limits and security

- Only HTTPS. Without HTTPS the endpoint answers 403. Only a site with the environment type `local` (`WP_ENVIRONMENT_TYPE`) may use HTTP, for tests.
- At most one request every 30 seconds, otherwise 429 with `Retry-After`. The program waits that long.
- After 10 wrong keys within 10 minutes the requesting address is locked for 15 minutes (429).
- The totals are kept for 60 seconds. Orders are read in pages of 200, so big shops do not run into the time limit.

## Troubleshooting

- **"Die Website antwortet nur über HTTPS"** (the site only answers over HTTPS): enter the address with `https://`. If the site has no certificate yet, set one up first.
- **"Das Plugin „SysopTV for WooCommerce“ antwortet nicht"** (the plugin does not answer, 404): is the plugin installed and active, and is the address right? The program also tries `?rest_route=/sysoptv/v1/stats` for sites without pretty permalinks.
- **"Zugriff verweigert (HTTP 403)"** (access denied): a security plugin or a firewall, for example a web application firewall of the host, blocks the REST API for visitors that are not logged in. Allow the route `/wp-json/sysoptv/v1/stats`, or the header `X-SysopTV-Key`.
- **"Die Antwort der Website ist nicht lesbar"** (the answer cannot be read): a caching plugin, a CDN or a maintenance mode serves an HTML page instead of the endpoint. Exclude `/wp-json/` from caching. The plugin already sends `Cache-Control: no-store`.
- **"Zu viele Anfragen"** (too many requests): the plugin allows one request every 30 seconds. "Shop testen" right after an automatic query has to wait a moment.
- **"Zu viele falsche Schlüssel, der Zugang ist für 15 Minuten gesperrt"** (too many wrong keys, locked for 15 minutes): wait 15 minutes, then enter the right key.
- **"Der Schlüssel fehlt oder ist falsch"** (the key is missing or wrong): create a new key under WooCommerce > SysopTV and enter it again. The cube shows "Schlüssel ungültig".
- **"Plugin aktualisieren"** or **"Jahr wird noch berechnet"** for the year: see [Updating the plugin](#updating-the-plugin) and [The year](#the-year).

## Removing the plugin

Deactivate and delete it in WordPress. Deleting removes everything the plugin stored: its options, including the key and the stored day values, and its temporary data. Orders are never touched.
