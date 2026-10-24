# Setting up Shopify for SysopTV

The Windows program reads the orders of your own Shopify shop. It needs an app in the Shopify Dev Dashboard with the access scope `read_orders`, installed on your shop, and the client ID and client secret of that app. The program gets a token with them itself (client credentials grant).

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
   - **Scopes**: `read_orders`.
2. Click **Release**. An app needs at least one version before it can be installed.

If you change the scopes later in a new version, they are not applied automatically to a shop where the app is already installed. You have to approve the new scopes in the Shopify admin.

About the scope, source: [Access scopes](https://shopify.dev/docs/api/usage/access-scopes): `read_orders` gives read access to the orders of the last 60 days. Older orders would need `read_all_orders` as well, SysopTV does not need them. Apps created in the Dev Dashboard under a merchant organization need no approval from Shopify for this scope; apps under a Partner organization have to request it in the Partner Dashboard first.

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

## How the program uses them

Source: [Client credentials grant](https://shopify.dev/docs/apps/build/authentication-authorization/client-credentials-grant)

- The program sends client ID and secret to `https://<shop>.myshopify.com/admin/oauth/access_token` and gets an access token that lasts 24 hours. It asks for a new one before that runs out. The token is not shown anywhere in the Shopify admin.
- This only works when the app and the shop belong to the same Shopify organization. Otherwise Shopify answers with `shop_not_permitted` ("Client credentials cannot be performed on this shop"). According to the documentation, typical reasons are a development store created outside the Dev Dashboard, or app and shop in different organizations.
- Queries go to the GraphQL Admin API, version `2026-10` ([GraphQL Admin API](https://shopify.dev/docs/api/admin-graphql)).

## Older custom apps

New custom apps can no longer be created in the shop admin. If you still have one, its Admin API access token works as well: enter it in the field "Admin-API-Zugriffstoken" instead of client ID and secret. See [Shopify: admin-created custom apps](https://shopify.dev/docs/apps/build/authentication-authorization/legacy/admin-custom-apps).
