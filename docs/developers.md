# Developers

This page is for anyone who wants to build SysopTV, write an own sender or design, or prepare a release.

You need:

- Git Bash, WSL or Linux for the build scripts
- for the firmware: arduino-cli
- for the Windows program: the .NET SDK 6 or newer, for the setup Inno Setup 6
- for the plugin: Python 3, optionally PHP for the syntax check

## Project structure

```
firmware/SysopTV/     firmware for the cube (ESP8266, Arduino)
app/SysopTV.Tray/     Windows program (C#, .NET Framework 4.8, WinForms)
app/assets/           icon and the fonts Inter and JetBrains Mono with their licences
installer/            Inno Setup script for the setup
wordpress/            plugin "SysopTV for WooCommerce" and tools/build-plugin.py
sender/               the older PowerShell sender
docs/                 this documentation, docs/images/ with pictures
build.sh              builds the firmware
build-app.sh          builds the Windows program, the ZIP and the setup
```

## Building

### Firmware

You need arduino-cli, the esp8266 core 3.1.2, TFT_eSPI 2.5.43 and ArduinoJson 7.4.3:

```sh
arduino-cli core install esp8266:esp8266@3.1.2 \
  --additional-urls https://arduino.esp8266.com/stable/package_esp8266com_index.json
arduino-cli lib install "TFT_eSPI@2.5.43"
arduino-cli lib install "ArduinoJson@7.4.3"
bash build.sh
```

The script checks that exactly these library versions are installed. The firmware ends up in `dist/sysoptv-<version>.bin`, the version comes from `firmware/SysopTV/config.h`. The display settings are passed as compiler flags, there is no need to edit `User_Setup.h` in TFT_eSPI.

`build.sh` stops if the image is larger than 500000 bytes. An update is first written next to the running firmware, and both have to fit into 1 MB. The original GeekMagic firmware is about 505 KB, so SysopTV has to stay small enough for it to fit next to it. Otherwise there would be no way back over WLAN.

The flash layout is 4M3M, the same as the original firmware: program and update buffer below 0x100000. The area of the original firmware from 0x100000 to 0x3FA000 is never written. SysopTV stores its settings in the 4 KB EEPROM sector at 0x3FB000.

Rules for every firmware change:

- The update page `/update` must work in every mode: normal operation, setup hotspot and rescue mode.
- Keep the crash protection: three crash restarts in a row switch to rescue mode, and new functions stay off in rescue mode.
- Do not change the flash layout, and never write between 0x100000 and 0x3FA000.

### Windows program

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e
winget install --id JRSoftware.InnoSetup -e
```

```sh
bash build-app.sh
```

The script puts `sysoptv-app-<version>-portable.zip` and, if Inno Setup is installed, `sysoptv-app-<version>-setup.exe` into `dist/`. The program version is set only in `app/SysopTV.Tray/SysopTV.Tray.csproj`, separate from the firmware version. The program uses only what .NET Framework 4.8 brings along, no extra packages.

### WordPress plugin

```sh
python wordpress/tools/build-plugin.py
```

The script writes the translation template, compiles the German translation (`.mo`) without extra tools, runs `php -l` on every file if PHP is installed, and puts `dist/sysoptv-woocommerce-<version>.zip` together. That ZIP contains the folder `sysoptv-woocommerce/` and can be uploaded in WordPress.

## Tests

Tests and trials never touch the real settings of a user. The program finds all its files through `AppPaths`. The environment variable `SYSOPTV_DATA_DIR` or the option `--data-dir <folder>` puts `config.json`, `state.json` and `app.log` into that folder.

- Every test uses a fresh temp folder through `SYSOPTV_DATA_DIR` and deletes it afterwards.
- Before the first write, a test checks that `AppPaths.ConfigDir` and `AppPaths.LogDir` point to that folder and not to `%APPDATA%\SysopTV` or `%LOCALAPPDATA%\SysopTV`, and stops otherwise.
- Tests never ask Anthropic, Shopify or a real shop. The service has replaceable delegates for that (`Query`, `FetchShopFrom`, `SendFrameTo`, `ReadInfo`).

`SysopTV.exe --docs <folder>` writes the pictures of this documentation: `<folder>\screens` with the designs and `<folder>\app` with every page of the window. It uses sample data, a fresh temp folder for all program files, and no network. Run it as `SysopTV.exe --docs docs\images`.

## Interfaces of the cube

The Windows program uses these. They are documented here for anyone who wants to write an own sender.

- `GET /api/info` (always open): status as JSON, for example `version`, `ip`, `uptime`, `deviceName`, `capabilities` (`["usage","settings","frame"]`), `design`, `frameValid`, `frameSource` and `frameAgeSeconds`. It never contains the password, its hash, the API key or the WLAN password.
- `POST /api/usage`: the Claude numbers as JSON, at most 512 bytes. Needs the header `X-SysopTV-Key` if an API key is set. Example:

  ```json
  {"state":"ok","now":1761146400,"tz":7200,
   "session":{"used":23,"resetsAt":1761149400},
   "week":{"used":85,"resetsAt":1761166800}}
  ```

  `state` is `ok`, `login` (login expired) or `error`. `now` is Unix seconds in UTC and `tz` the offset to UTC in seconds, both required for `ok`. `session` and `week` may be missing; `used` is a percentage, `resetsAt` Unix seconds or `null`. Answers: `200`, `400` with a short message, `401` for a missing or wrong key, `413` for a too large body, `503` in rescue mode.
- `GET /api/settings` and `POST /api/settings`: the display settings as JSON (`design`, `cycleSeconds`, `showUsed`, `brightness`, `night` with `enabled`, `from`, `to` and `brightness`, `deviceName`). Both need the admin password (HTTP Basic Auth) if one is set. `POST` only works with `Content-Type: application/json` and the header `X-SysopTV-Client: app`. All fields are optional and checked like on the settings page. The cube allows no cross origin requests, so web pages cannot use this interface.
- `POST /api/frame`: picture mode. The format is described in [Picture format](frame-format.md).

### Picture mode

Since firmware 0.4.0 a PC can draw the whole screen itself and send it as a picture, also as parts of the screen. The cube then only shows these pictures. Pictures have priority over numbers. Since firmware 0.4.2 numbers sent at the same time are kept: if no picture arrives for 3 minutes, the cube draws its own chosen design with them. Since firmware 0.4.3 the cube refuses parts with `409` and `full_frame_required` while its screen is not a complete picture from the PC; `/api/info` reports this as `frameValid`. Brightness and night mode also apply in picture mode.

## Interface of the plugin

`GET /wp-json/sysoptv/v1/stats` with the key in the header `X-SysopTV-Key`. The fields of the answer, the counting rules and the limits are described in [WooCommerce](woocommerce-setup.md#which-data-leaves-the-shop). The plugin is in `wordpress/sysoptv-woocommerce/`; the translation source is `languages/sysoptv-woocommerce-de_DE.po`.

## Rules for own designs

The panel of the cube shows very dark colours much too bright and with a green tint, and medium grey on a light background almost disappears. So every design keeps to these rules:

- Dark backgrounds are pure black (0,0,0).
- No large areas in dark grey or dark colours below about 60 per channel, unless they are meant as a visible area and bright enough.
- Text on light backgrounds is black. Muted text is at most (70,70,70).
- Content keeps at least 8 pixels from the edge of the picture.
- Amber looks yellow with too much green, so use about (255,110,0). Green looks mint with blue in it, so use green without blue.

Pictures are 240x240 pixels in RGB565 (`Canvas` in the program). A design of the program gets finished texts and values and knows nothing about network or settings. A new Claude design goes into `ClaudeDesigns.All` (new designs only at the end, the id is stored in `config.json`), a new big display style into `ShopBig.Styles`, a new detail view into `ShopDesigns.All`. A new design of the firmware is a new `view_<name>.cpp` with `invalidate()` and `show()`, an entry in `views.h`, `DesignId` and the table in `design.cpp`.

## Adding a new provider

A provider is a new source of numbers for a cube, like Shopify or WooCommerce. In the program:

1. A client class that reads the source and returns `ShopData` (or an own model for a new kind of display), never customer data. Errors go into `ShopData.Error` with an `ErrorKind`, so the cube can show a notice.
2. An entry in `CubeFunction.All` and, for a shop, in `ShopSources.All` (the same id). The id is stored in `config.json`.
3. The access in `AppConfig` with secrets encrypted through `Secret.Protect` (DPAPI).
4. A feed in `SenderService` (`feeds`, `FetchShopFrom`), so the source is asked only while a cube shows it, and the last valid values in `AppState`.
5. A page in the window: a class derived from `Page` (or `ShopPage` for a shop) and one line in `MainForm.CreatePages()`.
6. Offline tests with sample data and `SYSOPTV_DATA_DIR`.

## Release

1. Set the versions: firmware in `firmware/SysopTV/config.h`, program in `app/SysopTV.Tray/SysopTV.Tray.csproj`, plugin in the header of `wordpress/sysoptv-woocommerce/sysoptv-woocommerce.php`, in `SYSOPTV_WC_VERSION` and in `readme.txt` (with changelog).
2. Build: `bash build.sh`, `bash build-app.sh`, `python wordpress/tools/build-plugin.py`. The files land in `dist/`: `sysoptv-<firmware>.bin`, `sysoptv-app-<program>-setup.exe`, `sysoptv-app-<program>-portable.zip`, `sysoptv-woocommerce-<plugin>.zip`.
3. Run all offline tests (with `SYSOPTV_DATA_DIR`).
4. Renew the pictures of the documentation with `SysopTV.exe --docs docs\images` and check them.
5. Create the release on GitHub with a tag like `v0.7.0` and upload exactly the files from `dist/` named in step 2.
