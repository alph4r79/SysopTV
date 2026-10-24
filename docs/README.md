# SysopTV documentation

These guides take you from an unopened cube to a running display. Read them in this order:

1. [Installation](installation.md): put SysopTV on the GeekMagic SmallTV Ultra, connect it to your WLAN and set the admin password.
2. [Windows app](windows-app.md): install the program on your PC, find your cubes and choose what each one shows.
3. [Claude limits](claude.md): where the numbers come from, what session and week mean, and why the program does not ask more often.
4. [Shopify](shopify-setup.md): create the app in your own Shopify Dev Dashboard and connect your shop.
5. [WooCommerce](woocommerce-setup.md): install the WordPress plugin, create the key and connect your shop.
6. [Designs](designs.md): all designs, big displays and detail views with pictures, and the setting that belongs to each.
7. [Troubleshooting](troubleshooting.md): what to do when the cube is not found, shows something unexpected or you forgot the password.
8. [Privacy and security](privacy-security.md): which data goes where, what is stored on your PC and what never happens.
9. [Developers](developers.md): project structure, building, tests, the interfaces of the cube and the plugin, and the release steps.
10. [Picture format](frame-format.md): the format the program uses to send pictures to the cube.

Auf Deutsch: [Schnellstart](de/schnellstart.md) auf einer Seite.

## Please read first

- SysopTV is an unofficial project. It is not affiliated with Anthropic, Shopify, WooCommerce or GeekMagic.
- The Claude limits come from an undocumented endpoint that Claude Code uses as well. It can change or disappear at any time.
- Flashing is at your own risk. The cube has no USB data connection. If something goes wrong and the update page can no longer be reached, the only way back is a USB serial adapter and some soldering.

## Status

Firmware 0.5.0, Windows program 0.8.0, WordPress plugin "SysopTV for WooCommerce" 0.2.0.

Tested on real hardware with firmware 0.3.2:

- flashing through the web interface of the original firmware
- WLAN setup through the setup hotspot
- clock
- updates through the `/update` page
- admin password and the protection of the pages
- saving the settings
- brightness and night mode
- emergency reset of the password
- API key
- display without the PC (NO CARRIER)
- `/flashbackup`

Not yet tested on real hardware, so there is no promise that these work:

- going back to the original firmware
- the device name (new in firmware 0.4.0)
- `/api/settings` (new in 0.4.0)
- picture mode with `/api/frame` (new in 0.4.0)
- the valid picture: no parts on a screen the cube drew itself (new in 0.4.3)
- firmware 0.5.0 as a whole: only pictures, NO CARRIER with internet time after 3 minutes, `POST /api/nocarrier`
- the Windows program 0.8.0, including the firmware update from the program and NO CARRIER on exit and sleep
- the WordPress plugin "SysopTV for WooCommerce" 0.2.0
