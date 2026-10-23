# SysopTV

SysopTV is a free firmware for the GeekMagic SmallTV Ultra. It shows your Claude usage limits on your desk: the 5 hour session and the weekly limit, each with its reset time.

<table>
  <tr>
    <td align="center"><img src="docs/images/design-saeulen.jpg" width="250" alt="Design Säulen"><br>Säulen</td>
    <td align="center"><img src="docs/images/design-phosphor.jpg" width="250" alt="Design Phosphor"><br>Phosphor</td>
    <td align="center"><img src="docs/images/design-dos.jpg" width="250" alt="Design DOS"><br>DOS</td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/design-ansi.jpg" width="250" alt="Design ANSI"><br>ANSI</td>
    <td align="center"><img src="docs/images/design-bernstein.jpg" width="250" alt="Design Bernstein"><br>Bernstein</td>
    <td align="center"><img src="docs/images/design-klassik.jpg" width="250" alt="Design Klassik"><br>Klassik</td>
  </tr>
</table>

There are six designs: Säulen, Phosphor, DOS, ANSI, Bernstein and Klassik. Klassik follows the layout of the image that an older PC script made for the original firmware: the session big in the middle, the week as a bar of 16 segments below.

The name comes from the sysop, who in the days of dial-up mailboxes kept an eye on all systems from a single screen.

## Please read first

- This is an unofficial project. It is not affiliated with Anthropic or GeekMagic.
- The limits come from an undocumented endpoint that Claude Code uses as well. It can change or disappear at any time.
- Flashing is at your own risk. The device has no USB data connection. If something goes wrong and the update page can no longer be reached, the only way back is a USB serial adapter and some soldering.

## Status

Firmware version 0.4.1, Windows program version 0.3.0.

Tested on real hardware:

- flashing through the web interface of the original firmware
- WLAN setup through the setup hotspot
- all six designs with live data
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
- the device name (new in 0.4.0)
- `/api/settings` (new in 0.4.0)
- picture mode with `/api/frame` (new in 0.4.0)
- "nicht aktiv" for a session without reset time (new in 0.4.1)
- the Windows program 0.3.0

## How it works

The cube cannot fetch the limits by itself. A sender on your Windows PC reads the login key of Claude Code locally, asks for the limits and sends only the percentages and reset times to the cube over your local network. The key never leaves the PC and is never sent to the cube.

There are two senders, use only one of them:

- the Windows program SysopTV (`app/`), which runs in the notification area of the taskbar and has its own window
- the PowerShell script `sender/sysoptv-sender.ps1`, the older and smaller way

Both do the same. There is no script for the original firmware.

After a start the cube shows the "Ready" screen with its IP address. If no data arrives within 120 seconds, it switches to the display without PC: the clock, "NO CARRIER" and its IP address small at the bottom. The same happens during operation when no new data comes for 10 minutes. As soon as data arrives, the chosen design appears.

## Requirements

- GeekMagic SmallTV Ultra with ESP8266. Other models, for example the Pro or variants with ESP32, are not supported. Do not flash SysopTV onto them.
- A Windows PC in the same network: Windows 10 or 11 with .NET Framework 4.8 for the Windows program, or Windows PowerShell 5.1 for the script. Both are part of current Windows versions.
- Claude Code installed and logged in with a Claude subscription. The file `.credentials.json` must exist in the folder `.claude` of your user profile.

## Installing the firmware

1. Download `sysoptv-<version>.bin` from the GitHub Releases, or build it yourself (see "Building").
2. Open the web interface of the original GeekMagic firmware in your browser, using the IP address of the cube. At the bottom, under "Firmware update", select the .bin file and upload it.
3. After the restart SysopTV tries a saved WLAN for 20 seconds. The WLAN of the original firmware is not taken over, so on the first start it opens an open hotspot named `SysopTV-Setup-xxxx`, where `xxxx` are the last four hex digits of the chip ID of this cube.
4. Connect your phone or laptop to `SysopTV-Setup-xxxx` and open `http://192.168.4.1` in the browser. Pick your network (2.4 GHz only), enter the password and save. The cube restarts and shows its IP address on the display.
5. From then on SysopTV is reachable at that IP address or at `http://sysoptv-xxxx.local` (the same four hex digits). The name can be changed in the settings, see "Device name".
6. Give the cube a fixed IP address in your router. The sender needs the address, and it should not change.
7. First thing: set the admin password on the overview page. It needs at least 8 characters (at most 64). When the browser asks for the login, the user name does not matter.

If nobody connects to the hotspot for three minutes and a WLAN is saved, SysopTV restarts and tries the saved WLAN again, in case the router was just off for a moment.

## Windows program

The Windows program SysopTV does the same as the PowerShell script, but runs in the notification area of the taskbar and can be set up in a window. It asks for the limits every 120 seconds and sends them to the cube, with the same rules as the script for errors and for the server limit (see "When something fails").

It can feed several cubes. Each cube in the list gets one function:

- **Claude, Würfel zeichnet selbst** (Claude, the cube draws itself): the program sends only the numbers, the cube shows them in its own design. Works with every firmware.
- **Claude als Bild** (Claude as picture): the program draws one of the six designs of the firmware itself and sends it as a picture. Needs firmware 0.4.0 or newer (picture mode).
- **Shop als Bild** (shop as picture): orders and revenue of a Shopify shop as a picture, in one of four shop designs. Needs firmware 0.4.0 or newer and a Shopify access, see "Shopify".

The limits are fetched from Anthropic only once per round, no matter how many cubes show Claude. The pictures are drawn every second but only sent when something changes, and then only the changed parts of the screen (up to four, joined when one part is smaller). After a restart of the cube and at least every 10 minutes the whole picture goes out.

The program remembers the time of the last query, a pause after error 429 and the last valid values in `%LOCALAPPDATA%\SysopTV\state.json` (never the login key). After a restart it waits until the interval has passed and the pause is over, and shows the stored values until then.

Version 0.3.0 of the program is not yet tested on real hardware, except for the parts it took over from 0.1.0.

### Designs as pictures

For "Claude als Bild" the program draws the designs Säulen, Phosphor, DOS, ANSI, Bernstein and Klassik with the same colours, fonts and positions as firmware 0.4.1, including "nicht aktiv" and "ÜBRIG". Per cube you choose the design, used or remaining, and optionally an automatic change every 30 or 60 seconds, which the program does.

Values that are not fresh stay on the screen with a small "Stand HH:MM" (as of). An expired login shows "ANMELDUNG ABGELAUFEN" in the colours of the design. As long as there are no values at all, the program sends no picture, and after 3 minutes the cube shows its own display without PC. The same applies to the shop.

For "Shop als Bild" there are four designs, all with the 8x16 VGA font:

- **DOS**: table of today, yesterday, 7 days and the month with orders and revenue, the average, open orders and the last order.
- **Shop Heute** (shop today): today's orders, newest first, with time, order number, amount and a green tick for shipped or a yellow dot for open. If more orders come in than fit, the last line says how many more. Without orders it shows "Noch keine Bestellung heute" and when the last one came. At the bottom the number of orders and the revenue of today.
- **Kassenbon** (receipt): a receipt on light paper with today, yesterday, 7 days and month, the sum of today in double width, the open orders and a torn edge.
- **Shop Woche** (shop week): the revenue of the last 7 days in amber, the change to the week before, one bar per day with today on the right, and the number of orders with the average.

The icon in the taskbar shows the state by colour:

- green: data is being sent
- amber: less than 20 % left, or the login of Claude Code has expired
- red: no data from Anthropic, or the cube is not reachable
- grey: no data yet

Right-click the icon for the menu: current state, send now, open the cube in the browser, open the window, open the log, exit. A double-click opens the window.

### Download and installation

Download one of these files from the GitHub Releases:

- `sysoptv-app-<version>-setup.exe`: the setup. It installs without administrator rights to `%LOCALAPPDATA%\Programs\SysopTV`, adds an entry to the start menu and can start the program with Windows.
- `sysoptv-app-<version>-portable.zip`: the same program without setup. Unpack it anywhere and start `SysopTV.exe`.

The program is not signed. On the first start Windows SmartScreen may show "Der Computer wurde durch Windows geschützt" with "Unbekannter Herausgeber" (unknown publisher). Click "Weitere Informationen" (more info), then "Trotzdem ausführen" (run anyway). The warning appears because a code signing certificate costs money every year. The source code is open in this repository, and you can build the program yourself (see "Building").

### The window

At the top the window shows the state and the time of the last sending. On the left is a menu with these pages:

- **Übersicht** (overview): one line per cube with name, function, design, last sending and state in colour, below the Claude values and the shop values of today.
- **Würfel** (cubes): the list of cubes on the left with "Suchen" (search for cubes in your local network, a click on a result adds it), "Von Hand hinzufügen" (add by hand) and "Entfernen" (remove). On the right the selected cube: address, function, design, used or remaining, automatic change, the optional API key (see "Optional API key"), the admin password of the cube and "Verbindung testen" (test connection, shows firmware version and uptime). Below that the settings stored in the cube itself: brightness, night mode, device name, and for "Claude, Würfel zeichnet selbst" also its design, change and used or remaining. They are read with "Vom Würfel laden" and written with "Auf dem Würfel speichern" (firmware 0.4.0 or newer, otherwise a button opens the settings page of the cube in the browser).
- **Claude**: the interval in seconds (at least 120), the state of the login, the time of the last and the next query, an active pause after error 429 with the time left, and "Jetzt abfragen" (query now, at most once in 120 seconds).
- **Shopify**: shop address, the name for the display and the access (see "Shopify"), "Shop testen" (test the shop) and the last query.
- **Programm** (program): "Mit Windows starten" (start with Windows), the log folder, the version and the link to this repository.

Settings of the program are saved as soon as you leave a field, there is no save button. Only the settings stored in the cube need "Auf dem Würfel speichern". The window fits on a screen of 1366 x 768 pixels, can be made larger, and long pages scroll.

The settings are stored in `%APPDATA%\SysopTV\config.json`, API keys, admin passwords and the Shopify access only encrypted for your Windows user. Configurations of versions 0.1.0 and 0.2.0 are taken over automatically. Cubes from 0.2.0 with a picture function get the design DOS, "verbraucht" and no automatic change. The log is `%LOCALAPPDATA%\SysopTV\app.log` (at most 1 MB, then one backup `app.old.log`). The program only connects to `api.anthropic.com`, to your shop at Shopify (only if set up) and to the cubes, and the search only to addresses in your local network. There is no telemetry and no update check.

If the program finds the PowerShell script in the startup folder or running, it offers once per start to move the shortcut to `%APPDATA%\SysopTV\alt\`. Running scripts are not stopped, end them in the Task Manager or restart the PC.

For a test from the command line, `SysopTV.exe --once` queries the limits once, sends them to the first cube with "Claude, Würfel zeichnet selbst" and prints the result. `--device 192.168.178.50` sends to another address for this one run. `SysopTV.exe --preview C:\Pfad\zu\ordner` writes every picture design with sample data as PNG files, without network. `--once` also keeps to the 120 seconds after the last query and updates `state.json`.

### Shopify

The program reads the orders over the Shopify GraphQL Admin API, version `2026-10`. It needs the access scope `read_orders`. Shopify only returns the last 60 days without `read_all_orders`, which is enough for the current month and the last 14 days. Cancelled and test orders are not counted. The shop is asked every 5 minutes.

Of each order the program reads only the order number (like #1043), the time, the amount and whether it is shipped. It reads **no customer data**: no names, no addresses, no e-mail addresses.

From that it shows (in the time zone of the shop):

- orders and revenue for today, yesterday, the last 7 days and the current month
- today's orders, the latest 20 at most, with number, time, amount and shipped or not
- revenue and orders per day for the last 14 days
- the number of open orders that are not shipped yet, and time and amount of the last order

Shopify no longer lets you create new custom apps in the shop admin. For your own shop the official way is an app in the Shopify Dev Dashboard with the client credentials grant. Follow the official guides on shopify.dev, the menus there can change:

- [Create apps using the Dev Dashboard](https://shopify.dev/docs/apps/build/dev-dashboard/create-apps-using-dev-dashboard): create the app, give its version the scope `read_orders`, release it and install it on your shop.
- [Client credentials grant](https://shopify.dev/docs/apps/build/authentication-authorization/client-credentials-grant): the app settings show a client ID and a client secret. Enter both in SysopTV. The program gets a token with them itself, which lasts 24 hours and is renewed when needed. This only works if the app and the shop belong to the same Shopify organization.

If you still have an older custom app from the shop admin, its Admin API access token (it starts with `shpat_`) works as well. Enter it in the field "Admin-API-Zugriffstoken" instead of client ID and secret, see [Shopify: admin-created custom apps](https://shopify.dev/docs/apps/build/authentication-authorization/legacy/admin-custom-apps).

## Setting up the PowerShell script

Use the script instead of the Windows program, not together with it. Two senders would ask for the limits twice.

The sender is the file `sender/sysoptv-sender.ps1`. It needs nothing besides Windows PowerShell 5.1, which is part of Windows.

### 1. Copy it

Copy the folder `sender` to your PC, for example to `C:\Path\to\sender\`. The location is up to you.

### 2. Test it once

Run the test in PowerShell, not in the classic command prompt (cmd). If you have cmd open, type `powershell` first. The line then starts with `PS`.

Go to the folder and start a single run. Use the IP address of your cube instead of `192.168.178.50`:

```powershell
cd C:\Path\to\sender
powershell -ExecutionPolicy Bypass -File .\sysoptv-sender.ps1 -DeviceIp 192.168.178.50 -Once
```

The window shows what was read and sent, and whether the cube accepted it. A few seconds later the cube shows the limits.

If the window says that the sender is already running, another sender is active on this PC. Two senders would ask for the limits twice, so the second one stops.

### 3. Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-DeviceIp` | `sysoptv.local` | Address of the cube. An IP address is more reliable than the name. Since firmware 0.4.0 the name is `sysoptv-xxxx.local` (see "Device name"), so give the address explicitly. |
| `-IntervalSeconds` | `120` | Seconds between two queries. |
| `-ApiKey` | empty | Only needed if an API key is set in the cube, see below. |
| `-Once` | off | One single run with output in the window, for testing. |

> **Do not go below 120 seconds.** Querying every 60 seconds made the server answer with error 429 (too many requests) all the time. The script raises smaller values to 120 by itself. After a 429 it waits at least 5 minutes, or longer if the server asks for it.

### 4. Start it automatically

1. Press Win+R, type `shell:startup` and press Enter. The startup folder opens.
2. Right-click in the folder, choose "New", then "Shortcut".
3. Enter this as the target, with your own path and IP address:

   ```
   C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\Path\to\sender\sysoptv-sender.ps1" -DeviceIp 192.168.178.50
   ```

4. Name it, for example "SysopTV Sender". From the next login on the sender runs in the background without a window.

The sender writes a log to `%LOCALAPPDATA%\SysopTV\sender.log`. It only notes changes, errors and when the cube becomes reachable or unreachable. At 200 KB the log is renamed to `sender.log.old` and a new one is started.

### Login expired

The login key of Claude Code runs out after a few hours. It is only renewed when you use Claude Code. If the cube shows "ANMELDUNG ABGELAUFEN" (login expired), simply use Claude Code for a moment. The sender never renews the key by itself, on purpose: renewing it changes the stored login of Claude Code, and a mistake there could log you out.

### When something fails

A single failed query does not change the display. The sender keeps sending the last valid values with the current time, so the cube knows that the PC is still running. It only reports an error after 3 failed queries in a row, or when the last valid values are older than 15 minutes. The cube then shows "NO CARRIER". Rounds in which the sender waits because of a 429 do not count as failed queries, only the age of the values counts then.

### Optional API key

Without an API key, anyone in your network can send values to the cube. With a key, only a sender that knows the key may do that.

1. Open "Einstellungen" (settings) in the web interface and press "Neuen Schlüssel erzeugen" (create new key).
2. Windows program: enter the key under "API-Schlüssel" on the page "Würfel" of its window. PowerShell script: add `-ApiKey <key>` to the test command and to the startup shortcut.

If the key is missing or wrong, the cube answers with error 401 and the log of the sender says so.

## Using the web interface

The pages are in German. At the top there are three links: Übersicht (overview), Einstellungen (settings) and Update.

**Übersicht** (overview, `/`) shows:

- session and week with percentage, a bar and the reset time as on the display
- when the last data from the PC arrived, or a hint if no data comes, the login has expired or the sender cannot reach Anthropic
- the active design and whether the automatic change is on
- the device: WLAN, IP address, mDNS name, signal, whether the clock is set, uptime, free memory, reason of the last restart
- the link to the flash backup

The values refresh every 10 seconds. Without a password the page also offers the form to set the admin password.

**Einstellungen** (settings, `/settings`):

- design: Säulen, Phosphor, DOS, ANSI, Bernstein or Klassik
- automatic change: off, every 30 or every 60 seconds
- show percentages as used ("verbraucht", the default, like on claude.ai) or remaining ("übrig"). The warning colour always appears when less than 20 % are remaining.
- brightness 10 to 100 %
- night mode with a start and end time and its own brightness from 0 to 100 % (0 switches the display off). It needs the time, from the PC or from the internet (NTP).
- create, show or delete the API key
- change the admin password

The settings are stored permanently in the cube and are only written when something changes.

**Update** (`/update`) installs a new firmware file. Going back to the original firmware has not been tested yet, so download the flash backup from the overview first. If an update fails, the current firmware stays intact and SysopTV restarts after a few seconds. Do not cut the power during an update.

**Flash backup** (`/flashbackup`, link on the overview) downloads a copy of the flash area 0x100000 to 0x3FA000. That is where the original firmware keeps its files, including its own web interface. It is only available once an admin password is set, because it may contain login data of the original firmware. Keep it to yourself.

The clock comes from the PC. When no data from the PC has arrived for 15 minutes, SysopTV uses internet time (NTP, pool.ntp.org) with the time zone of Germany (Europe/Berlin, including daylight saving time).

## Device name

Since firmware 0.4.0 every cube has a device name, so several cubes can run in one network. The default is `sysoptv-` plus the last four hex digits of the chip ID, for example `sysoptv-3fa2`. The cube is then reachable at `http://sysoptv-3fa2.local`, and its setup hotspot is called `SysopTV-Setup-3fa2`.

The name can be changed under "Einstellungen" (settings), section "Gerät" (device): letters, digits and hyphen, 1 to 24 characters. A new name takes effect at once for the `.local` address. The overview shows the name.

## Interfaces for the Windows program

These are used by the Windows program. They are documented here for anyone who wants to write their own sender.

- `GET /api/info` (always open): status as JSON. Since 0.4.0 also `deviceName` and `capabilities` (`["usage","settings","frame"]`).
- `POST /api/usage`: usage data from the sender, see the existing senders. Needs the header `X-SysopTV-Key` if an API key is set.
- `GET /api/settings` and `POST /api/settings`: the display settings as JSON (design, automatic change, used or remaining, brightness, night mode, device name). Both need the admin password (HTTP Basic Auth) if one is set. `POST` only works with `Content-Type: application/json` and the header `X-SysopTV-Client: app`, otherwise the cube answers 400. All fields are optional, the values are checked like on the settings page. The answer contains the stored values. The cube allows no cross-origin requests (no CORS), so web pages cannot use this interface.
- `POST /api/frame`: picture mode, see below.

## Picture mode

Since firmware 0.4.0 a PC can draw the whole screen itself and send it as a picture. The cube then only shows these pictures and does not draw its own designs over them. If no picture arrives for 3 minutes, it shows its own display without PC (clock or NO CARRIER with the IP address). If usage data arrives again instead of pictures, it draws its own designs again. Brightness and night mode also apply in picture mode.

The pictures are sent as a small, run length compressed format, also as parts of the screen. The format is described in [docs/frame-format.md](docs/frame-format.md).

## Emergencies

### Forgotten password

Unplug the cube three times in a row, each time within 10 seconds after plugging it in. On the fourth start the password, the API key and all settings are reset. The WLAN stays. The display shows "Passwort zurückgesetzt" (password reset) for a moment. Only starts after plugging in count, crashes and restarts do not.

### Rescue mode

If the firmware crashes three times in a row, SysopTV starts in rescue mode. It ignores the settings and only offers WLAN, the overview and the update page, so you can install a working version. The update page keeps the admin password, if it can still be read.

### Back to the original firmware

This has not been tested yet, so there are no instructions here for now.

## Building

### Firmware

You need arduino-cli, the esp8266 core 3.1.2, TFT_eSPI 2.5.43 and ArduinoJson 7.4.3:

```sh
arduino-cli core install esp8266:esp8266@3.1.2 \
  --additional-urls https://arduino.esp8266.com/stable/package_esp8266com_index.json
arduino-cli lib install "TFT_eSPI@2.5.43"
arduino-cli lib install "ArduinoJson@7.4.3"
```

Then run the build script in Git Bash (on Windows), WSL or Linux:

```sh
bash build.sh
```

The script checks that exactly these library versions are installed. The firmware ends up in `dist/sysoptv-<version>.bin`. The display settings are passed as compiler flags, so there is no need to edit `User_Setup.h` in TFT_eSPI.

`build.sh` stops if the image is larger than 500000 bytes. An update is first written next to the running firmware, and both have to fit into 1 MB. The original GeekMagic firmware is about 505 KB, so SysopTV has to stay small enough for it to fit next to it. Otherwise there would be no way back over WLAN.

The flash layout is 4M3M, the same as the original firmware: program and update buffer below 0x100000, the area of the original firmware from 0x100000 to 0x3FA000 is never written. SysopTV stores its settings in the 4 KB EEPROM sector at 0x3FB000.

### Windows program

You need the .NET SDK (version 6 or newer, it builds for .NET Framework 4.8) and for the setup Inno Setup 6:

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e
winget install --id JRSoftware.InnoSetup -e
```

Then run in Git Bash:

```sh
bash build-app.sh
```

The script builds the program and puts `sysoptv-app-<version>-portable.zip` and, if Inno Setup is installed, `sysoptv-app-<version>-setup.exe` into `dist/`. The program version is set in `app/SysopTV.Tray/SysopTV.Tray.csproj`, it is separate from the firmware version. The program uses only what .NET Framework 4.8 brings along, no extra packages.

### Hardware

Hardware for contributors: ESP8266 (ESP-12F), 4 MB flash, 26 MHz crystal, ST7789 display with 240x240 pixels without CS line in SPI mode 3. Pins: MOSI 13, SCLK 14, DC 0, RST 2, backlight 5 (active low).

## Plans

Later the cube could show data of other services as well. The window of the program is built for that: a new service gets its own page in the side menu.

## License and thanks

The firmware, the Windows program and the PowerShell script are licensed under the GNU General Public License v3.0, see [LICENSE](LICENSE).

The embedded font in `firmware/SysopTV/font_vga.h` is **not** under the GPL. It is converted from "Px437 IBM VGA 8x16" of [The Ultimate Oldschool PC Font Pack](https://int10h.org/oldschool-pc-fonts/) by VileR and licensed under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). The same applies to `app/SysopTV.Tray/VgaFont.cs`, which adds the euro sign from "PxPlus IBM VGA 8x16" of the same font pack.

`app/SysopTV.Tray/TftFonts.cs` contains the fonts of TFT_eSPI 2.5.43 as plain bitmaps, so the pictures of the program look like the display: the GLCD font of Adafruit_GFX (Copyright (c) 2012 Adafruit Industries, BSD license) and Font 2, 4 and 8 of TFT_eSPI (Copyright (c) 2023 Bodmer, FreeBSD license). The license texts are in the head of the file.

Thanks to:

- the [ESP8266 Arduino core](https://github.com/esp8266/Arduino)
- [TFT_eSPI](https://github.com/Bodmer/TFT_eSPI) by Bodmer
- [ArduinoJson](https://github.com/bblanchon/ArduinoJson) by Benoît Blanchon
- the [ESPHome device documentation for the SmallTV Ultra](https://devices.esphome.io/devices/geekmagic-ultra/), for the pin assignment
- the [GeekMagic firmware repository](https://github.com/GeekMagicClock/smalltv-ultra), for the original firmware
- [CodexBar](https://github.com/steipete/CodexBar), the model for how the usage limits are queried

Claude is a trademark of Anthropic. GeekMagic is a trademark of GeekMagic.

## Auf Deutsch

SysopTV ist eine freie Firmware für den GeekMagic SmallTV Ultra mit ESP8266. Sie zeigt am Schreibtisch die Claude-Nutzungslimits an, die 5-Stunden-Session und die Woche, jeweils mit Reset-Zeit. Es ist ein inoffizielles Projekt ohne Verbindung zu Anthropic oder GeekMagic. Die Limits kommen über einen nicht dokumentierten Endpunkt, der sich jederzeit ändern kann.

Aufgespielt wird über „Firmware update“ unten in der Weboberfläche der Original-Firmware. Danach richtet man das WLAN über den Hotspot `SysopTV-Setup` und `192.168.4.1` ein und legt als Erstes das Admin-Passwort fest. Die Werte schickt das Sende-Skript im Ordner `sender/` vom Windows-PC, der Anmeldeschlüssel von Claude Code verlässt dabei nie den PC. Das Gerät hat keinen USB-Datenanschluss: Geht beim Flashen etwas schief und die Update-Seite ist nicht mehr erreichbar, hilft nur ein USB-Seriell-Adapter mit Lötarbeit. Die Benutzung ist auf eigenes Risiko.
