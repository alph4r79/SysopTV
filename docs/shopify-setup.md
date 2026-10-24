# Shopify

At the end of this guide a cube shows the orders and revenue of your Shopify shop, including the whole year.

You need:

- a Shopify shop and the Shopify account that owns it
- the Windows program and a cube with firmware 0.4.0 or newer, see [Windows app](windows-app.md)
- about 15 minutes

The steps in the Shopify Dev Dashboard follow the official Shopify documentation. Menus at Shopify can change; if something looks different, the linked pages are the reference.

## How it works

Every shop needs its own app in its own Dev Dashboard, with its own client ID and client secret. SysopTV does not have one central app.

The program uses the client credentials grant: it sends client ID and secret to your shop and gets an access token that lasts 24 hours. This only works when the app and the shop belong to the same Shopify organization. So create the app with the account that owns the shop. Otherwise Shopify answers with `shop_not_permitted`. Source: [Client credentials grant](https://shopify.dev/docs/apps/build/authentication-authorization/client-credentials-grant).

## 1. Create the app

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

1. Log in with the account that owns the shop and open the [Dev Dashboard](https://dev.shopify.com/dashboard/).
2. In the left navigation choose **Apps**, then click **Create app** at the top right.
3. Choose **Start from Dev Dashboard**, enter a name, for example "SysopTV", and click **Create**.

## 2. Configure the version

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

On the **Versions** page of the app fill in:

1. **App URL**: any valid address. SysopTV has no page of its own at Shopify. The Shopify documentation names `https://shopify.dev/apps/default-app-home` for apps that are not embedded in the Shopify admin; the address of this repository, `https://github.com/alph4r79/SysopTV`, works as well.
2. **Webhooks API version**: usually the newest version.
3. **Scopes**: `read_orders` and `read_all_orders`.
4. The app is not embedded in the Shopify admin. If the form offers to embed the app in the Shopify admin, switch it off. Source: the setting `embedded` in [App configuration](https://shopify.dev/docs/apps/build/cli-for-apps/app-configuration).
5. No redirect URLs are needed. The client credentials grant has no redirect flow. Source: [Client credentials grant](https://shopify.dev/docs/apps/build/authentication-authorization/client-credentials-grant).
6. Click **Release**. An app needs a released version before it can be installed.

### Why read_all_orders

`read_orders` only gives the orders of the last 60 days. Today, this week and this month work with it. The period "Dieses Jahr" (this year) counts all orders since 1 January and needs `read_all_orders` as well. Apps created in the Dev Dashboard under the organization of the shop owner need no approval from Shopify for this scope. Source: [Access scopes](https://shopify.dev/docs/api/usage/access-scopes).

## 3. Install the app in your shop

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

1. Open the app and click **Install app** at the top of the **Installs** card. The three dot menu in the row of the app in the **Apps** list works as well.
2. A new window opens. Choose your shop.
3. Click **Install**.

## 4. Copy client ID and secret

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

1. Open the app and click **App settings**.
2. The **Credentials** card shows the client ID and the client secret. Copy both.

The client secret belongs only into your own SysopTV program. Do not send it to anyone and do not post it. The client ID is not a secret.

## 5. Enter them in the program

1. Open the window of SysopTV and the page **Shopify**.
2. **Shop-Adresse** (shop address): the address of your shop at Shopify, for example `myshop.myshopify.com`.
3. **oder Client-ID** and **und Client-Secret**: the two values from step 4. Leave "Admin-API-Zugriffstoken" empty.
4. **Name für die Anzeige** (name for the display): the name the cube shows on top, for example "My Shop". Empty means the name of the shop at Shopify.
5. Press **Shop testen** (test the shop). The line next to it shows the shop name, today's orders and the API version, or what went wrong.
6. On the page **Würfel** choose the function **Shopify** for a cube, then the view and the style, see [Windows app](windows-app.md#verbindung-und-funktion-connection-and-function).

The program asks the shop every 5 minutes.

## Adding a scope later

If your app only has `read_orders`, the program shows "Für das Jahr fehlt der Zugriff read_all_orders" (access read_all_orders is missing for the year) on the page Shopify and, for the year, on the cube. Today, week and month keep working.

A new version alone is not enough. From experience, the new scope only takes effect after the app is installed again:

1. In the Dev Dashboard create a new version with the scopes `read_orders` and `read_all_orders` and click **Release**.
2. In the Shopify admin uninstall the app: **Settings > Apps**, then for the app click the menu icon and **Uninstall**, then **Uninstall** again. Source: [Uninstalling apps](https://help.shopify.com/en/manual/apps/uninstalling-apps).
3. Install the app again, see [step 3](#3-install-the-app-in-your-shop).
4. Restart the SysopTV program (right-click on its icon, "Beenden", then start it again), so it gets a new token with the new scope.

Client ID and secret stay the same, you do not have to enter them again. While the app is not installed, the program reports "Zugang abgelehnt" (access refused) for a short time. That is normal and goes away after the new installation.

## What the program reads

The program reads the orders over the Shopify GraphQL Admin API, version `2026-10`. Of each order it reads only the order number (like #1043), the time, the amount and whether it is shipped. It reads **no customer data**: no names, no addresses, no e-mail addresses. Cancelled and test orders are not counted. Revenue is the order total (`totalPriceSet`): gross, including shipping and taxes, after discounts, refunds not deducted.

From that it shows, in the time zone of the shop:

- orders and revenue for today, yesterday, the last 7 days, this week, this month and this year
- today's orders, the latest 20 at most, with number, time, amount and shipped or not
- revenue and orders per day for the last 14 days
- the number of orders not shipped yet, and time and amount of the last order

Each query reads only today and the last 30 days. For the year the program keeps the number of orders and the revenue per finished day in `state.json` and reads the whole year again once a day, in pages and within the limits of Shopify.

## Older custom apps

New custom apps can no longer be created in the shop admin. If you still have one, its Admin API access token works as well: enter it in the field "Admin-API-Zugriffstoken" instead of client ID and secret. See [Shopify: admin-created custom apps](https://shopify.dev/docs/apps/build/authentication-authorization/legacy/admin-custom-apps).

## Troubleshooting

- **"Shopify erlaubt Client-ID und Secret für diesen Shop nicht"** (`shop_not_permitted`): the app and the shop are not in the same organization. Create the app with the account that owns the shop. According to Shopify, a typical cause is also a development store created outside the Dev Dashboard.
- **"Zugang abgelehnt"** or **"Anmeldung abgelehnt"** (access or login refused): client ID or secret are wrong, or the app is not installed in the shop. Check both values and step 3. During a new installation of the app this message is normal for a short time.
- **"Shop nicht gefunden"** (shop not found): check the shop address, it ends with `.myshopify.com`.
- **"Der App fehlt der Scope read_orders"** (the app lacks the scope read_orders): add the scope, release a new version, then uninstall and install the app again in the shop, see [Adding a scope later](#adding-a-scope-later). Client ID and secret stay the same.
- **The year shows "Für das Jahr fehlt der Zugriff read_all_orders"**: the same steps for `read_all_orders`, see [Adding a scope later](#adding-a-scope-later). The page Shopify of the program says the same.
- **The year comes later than today**: with the first query of a day the program reads all orders of the year again. With many orders this takes a while, the other values appear together with it. The note "Jahr wird noch berechnet" (year is still being counted) only exists for WooCommerce.
- **"Zu viele Anfragen"** (too many requests): Shopify asked to wait. The program waits and tries again; the cube shows the time of the next try.
- **The cube shows "Shop nicht eingetragen"** (shop not set up): shop address or access are missing on the page Shopify.
