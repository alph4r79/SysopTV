=== SysopTV for WooCommerce ===
Contributors: alph4r79
Tags: woocommerce, orders, revenue, dashboard, display
Requires at least: 6.0
Tested up to: 6.8
Requires PHP: 7.4
WC requires at least: 7.1
Stable tag: 0.1.0
License: GPLv3 or later
License URI: https://www.gnu.org/licenses/gpl-3.0.html

Shows orders and revenue of your WooCommerce shop on a SysopTV desk cube, through a small key protected endpoint with totals only.

== Description ==

SysopTV is a free firmware and Windows program for a small desk cube with a 240 x 240 pixel display (GeekMagic SmallTV Ultra). This plugin lets the SysopTV program show the orders and revenue of your WooCommerce shop on the cube.

The plugin adds one REST endpoint, `GET /wp-json/sysoptv/v1/stats`. It is protected by a key that you create on the settings page (WooCommerce, SysopTV). Only a hash of the key is stored.

What the endpoint returns:

* shop name, currency and time zone
* number of orders and revenue for today, yesterday, this week (from Monday), this month, the last 7 days and each of the last 14 days
* the number of orders still to ship
* time and amount of the last order
* for today's orders (at most 20): order number, time, amount and whether it is shipped

No customer data leaves the shop: no names, addresses, e-mail addresses, phone numbers, IP addresses, notes or items.

How orders are counted:

* Orders with the status processing, completed and on-hold count. Processing also counts as still to ship. Completed counts as shipped.
* Cancelled, refunded, failed, pending, draft and trashed orders do not count.
* Revenue is the order total as WooCommerce shows it: gross, including shipping and taxes, after discounts. Refunds are not deducted.
* Days, weeks and months follow the time zone in the WordPress settings.

Security:

* The endpoint only answers over HTTPS. Only a site with the environment type "local" may use HTTP.
* At most one request every 30 seconds. After 10 wrong keys within 10 minutes the requesting address is locked for 15 minutes.
* The totals are cached for 60 seconds.

The plugin reads orders only through the WooCommerce API and works with the high performance order storage (HPOS) as well as with the classic storage.

== Installation ==

1. Upload the plugin with Plugins, Add New, Upload Plugin, and activate it. WooCommerce must be active.
2. Open WooCommerce, SysopTV and press "Create key".
3. Copy the key, it is shown only once, and enter it with the address of your website on the page WooCommerce of the SysopTV program.
4. Press "Shop testen" in the program.

== Frequently Asked Questions ==

= The program reports that the plugin is not installed =

Some security or firewall plugins block the REST API for visitors that are not logged in. Allow the route `/wp-json/sysoptv/v1/stats`.

= The program reports a broken answer =

A caching plugin or a proxy may serve a cached page instead of the endpoint. Exclude `/wp-json/` from caching.

= Can I see the key again? =

No, only a hash is stored. Create a new key and enter it in the program.

== Changelog ==

= 0.1.0 =
* First version.
