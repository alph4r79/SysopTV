# Setting up Shopify for SysopTV

The Windows program reads the orders of your own Shopify shop. It needs an app in the Shopify Dev Dashboard with the access scopes `read_orders` and `read_all_orders`, installed on your shop, and the client ID and client secret of that app. The program gets a token with them itself (client credentials grant).

All steps below are taken from the official Shopify documentation, checked on 24.10.2026. The pages show no date of their last update. Menus at Shopify can change, so if something looks different, the linked pages are the reference.

## 1. Create the app

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

1. Open the [Dev Dashboard](https://dev.shopify.com/dashboard/). In the left navigation **Apps** must be selected. Click **Create app** at the top right.
2. Choose **Start from Dev Dashboard**.
3. Enter a name for the app and click **Create**.

## 2. Set the scope and release a version

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

1. On the **Versions** page of the app fill in what is needed:
   - **App URL**: SysopTV is not embedded in the Shopify admin, so the documentation's placeholder `https://shopify.dev/apps/default-app-home` is enough.
   - **Webhooks API version**: usually the newest version.
   - **Scopes**: `read_orders` and `read_all_orders`.
2. Click **Release**. An app needs at least one version before it can be installed.

If you change the scopes later in a new version, they are not applied automatically to a shop where the app is already installed. You have to approve the new scopes in the Shopify admin.

About the scopes, source: [Access scopes](https://shopify.dev/docs/api/usage/access-scopes): `read_orders` gives read access to the orders of the last 60 days. Today, this week and this month work with it. The period "Dieses Jahr" counts all orders since 1 January and needs `read_all_orders` as well. Apps created in the Dev Dashboard under a merchant organization need no approval from Shopify for this scope; apps under a Partner organization have to request it in the Partner Dashboard first.

### Adding `read_all_orders` to an existing app

If your app only has `read_orders`, SysopTV shows "Für das Jahr fehlt der Zugriff read_all_orders" on the page **Shopify** and, for the year, on the cube. Today, week and month keep working. For an app in the Dev Dashboard under your own merchant organization there is no review by Shopify:

1. Open the app in the [Dev Dashboard](https://dev.shopify.com/dashboard/) and create a new version on the **Versions** page with the scopes `read_orders` and `read_all_orders`.
2. Click **Release**.
3. Approve the new scope in the Shopify admin of your shop. Shopify shows the request there because the installed app now asks for more access.
4. In SysopTV press **Shop testen** on the page **Shopify**. The row "Dieses Jahr" shows the numbers of the year.

SysopTV reads the scopes of the app with each query, so the note goes away by itself once the scope is approved.

## 3. Install the app on your shop

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

1. Open the app and click **Install app** at the top of the **Installs** card. The three dot menu (**⋯**) in the row of the app in the **Apps** list works as well.
2. A new tab opens. Choose your shop.
3. Click **Install**.

## 4. Copy client ID and secret

Source: [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard)

1. Open the app in the Dev Dashboard and click **App settings**.
2. The **Credentials** card shows the client ID and the hidden client secret. Copy both.
3. Enter them in SysopTV on the page **Shopify** under "Client-ID" and "Client-Secret", together with the shop address (for example `meinshop.myshopify.com`), then press **Shop testen**.
4. On the page **Würfel** choose the function **Shopify** for a cube. Without the access the page shows a short hint with a link to the page **Shopify**, and the cube shows "Shop nicht eingetragen".

## How the program uses them

Source: [Client credentials grant](https://shopify.dev/docs/apps/build/authentication-authorization/client-credentials-grant)

- The program sends client ID and secret to `https://<shop>.myshopify.com/admin/oauth/access_token` and gets an access token that lasts 24 hours. It asks for a new one before that runs out. The token is not shown anywhere in the Shopify admin.
- This only works when the app and the shop belong to the same Shopify organization. Otherwise Shopify answers with `shop_not_permitted` ("Client credentials cannot be performed on this shop"). According to the documentation, typical reasons are a development store created outside the Dev Dashboard, or app and shop in different organizations.
- Queries go to the GraphQL Admin API, version `2026-10` ([GraphQL Admin API](https://shopify.dev/docs/api/admin-graphql)).
- Each query reads the orders of today and the last 30 days. The year adds the day values of the older days of the year, which the program keeps in `state.json` (only number of orders and revenue per day). Once a day, with the first query after midnight in the time zone of the shop, it reads the whole year again in pages of 250 orders, so changes to older orders are counted too. Shopify reports the remaining query cost with each answer; when it runs low, the program waits before the next page. A new year starts the count again.

## Older custom apps

New custom apps can no longer be created in the shop admin. If you still have one, its Admin API access token works as well: enter it in the field "Admin-API-Zugriffstoken" instead of client ID and secret. See [Shopify: admin-created custom apps](https://shopify.dev/docs/apps/build/authentication-authorization/legacy/admin-custom-apps).
