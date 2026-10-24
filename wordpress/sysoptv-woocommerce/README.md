# SysopTV for WooCommerce

WordPress plugin that gives the SysopTV Windows program the orders and revenue of a WooCommerce shop, so a SysopTV cube can show them. Version 0.1.0, GPL-3.0-or-later.

- One endpoint, `GET /wp-json/sysoptv/v1/stats`, with the key in the header `X-SysopTV-Key`.
- Totals only, no customer data. The fields are described in [docs/woocommerce-setup.md](../../docs/woocommerce-setup.md).
- Only HTTPS (HTTP only on a site with the environment type "local"), at most one request every 30 seconds, a lock after 10 wrong keys, a cache of 60 seconds.
- Orders are read through `wc_get_orders` and `WC_Order` only, so the plugin works with the high performance order storage (HPOS).
- English with a German translation in `languages/`.

Build the installable ZIP from the root of the repository:

```sh
python wordpress/tools/build-plugin.py
```

The script writes the `.pot`, compiles the German `.mo`, runs `php -l` if PHP is installed and puts `dist/sysoptv-woocommerce-<version>.zip` together. The ZIP contains the folder `sysoptv-woocommerce/` and can be installed with Plugins, Add New, Upload Plugin.
