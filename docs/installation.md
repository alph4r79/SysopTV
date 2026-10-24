# Installation

At the end of this guide your cube runs SysopTV, is connected to your WLAN and is protected by an admin password.

You need:

- a GeekMagic SmallTV Ultra with ESP8266. Other models, for example the Pro or variants with ESP32, are not supported. Do not flash SysopTV onto them.
- a WLAN with 2.4 GHz
- a phone or laptop with a browser
- the firmware file `sysoptv-<version>.bin` from the [GitHub Releases](https://github.com/alph4r79/SysopTV/releases), or built yourself (see [Developers](developers.md#firmware))

> **Please read first.** Flashing is at your own risk. The cube has no USB data connection. If something goes wrong and the update page can no longer be reached, the only way back is a USB serial adapter and some soldering. Going back to the original firmware has not been tested yet, see [Troubleshooting](troubleshooting.md#back-to-the-original-firmware).

## 1. Install the firmware

1. Download `sysoptv-<version>.bin`.
2. Open the web interface of the original GeekMagic firmware in your browser, with the IP address of the cube. You find the address in the list of devices of your router.
3. At the bottom of the page, under "Firmware update", choose the `.bin` file and upload it.
4. The cube restarts. For 2 seconds it shows "SysopTV" and the version.

## 2. Connect it to your WLAN

SysopTV tries a saved WLAN for 20 seconds. The WLAN of the original firmware is not taken over, so on the first start it opens its own hotspot.

1. On your phone or laptop, connect to the open WLAN `SysopTV-Setup-xxxx`. The `xxxx` are the last four hex digits of the chip ID of this cube, for example `SysopTV-Setup-3fa2`.
2. Open `http://192.168.4.1` in the browser.
3. Choose your WLAN under "WLAN". A hidden network can be typed in under "Oder Name von Hand eingeben" (or enter the name by hand).
4. Enter the password under "Passwort" and press "Speichern und verbinden" (save and connect). Leave the password empty for an open network.
5. The cube restarts, connects and shows "Bereit" (ready) with its IP address.

Your network is not in the list? Press "Netze neu suchen" (search networks again). The cube restarts and scans again, then connect to the hotspot once more.

If nobody connects to the hotspot for three minutes and a WLAN is saved, SysopTV restarts and tries the saved WLAN again, in case the router was just off for a moment.

## 3. Give the cube a fixed address

From now on the cube is reachable at its IP address, for example `http://192.168.178.50`, and at `http://sysoptv-xxxx.local` with the same four hex digits.

Give the cube a fixed IP address in your router. The Windows program needs the address, and it should not change. How this works depends on your router, see its manual.

## 4. Set the admin password

Do this first. Without a password anyone in your network can change the settings of the cube.

1. Open the address of the cube in the browser. The page "Übersicht" (overview) opens.
2. Under "Admin-Passwort" enter a password twice and press "Passwort festlegen" (set password). It needs 8 to 64 characters.
3. From now on the browser asks for a login on protected pages. The user name does not matter, only the password counts.

Forgot the password? See [Troubleshooting](troubleshooting.md#forgotten-password).

## The web interface of the cube

The pages are in German. At the top there are three links: Übersicht (overview), Einstellungen (settings) and Update.

**Übersicht** (`/`) shows:

- session and week with percentage, a bar and the reset time as on the display
- when the last data from the PC arrived, or a hint if no data comes, the login has expired or the PC cannot reach Anthropic
- in picture mode "Bildmodus" with the name of the picture and its age
- the active design and whether the automatic change is on
- the device: WLAN, IP address, mDNS name, signal, whether the clock is set, uptime, free memory, reason of the last restart
- under "Wartung" (maintenance) the link "Flash-Sicherung herunterladen" (download flash backup)

The values refresh every 10 seconds.

**Einstellungen** (`/settings`):

- design: Säulen, Phosphor, DOS, ANSI, Bernstein or Klassik
- automatic change: off, every 30 or every 60 seconds
- show the percentages as used ("verbraucht", the default, like on claude.ai) or remaining ("übrig"). The warning colour appears when less than 20 % are left.
- brightness 10 to 100 %
- night mode with a start and an end time and its own brightness from 0 to 100 % (0 switches the display off). It needs the time, from the PC or from the internet.
- device name, see below
- API key: "Neuen Schlüssel erzeugen" (create new key) and "Schlüssel löschen" (delete key), see [Privacy and security](privacy-security.md#api-key-of-the-cube)
- change the admin password

Press "Speichern" (save). The settings stay in the cube and are only written when something changed. With the Windows program, the design of the cube is only the fall back display, see [Windows app](windows-app.md#einstellungen-im-würfel-settings-in-the-cube).

**Update** (`/update`), see below.

## Device name

Every cube has a device name, so several cubes can run in one network. The default is `sysoptv-` plus the last four hex digits of the chip ID, for example `sysoptv-3fa2`. The cube is then reachable at `http://sysoptv-3fa2.local`, and its setup hotspot is called `SysopTV-Setup-3fa2`.

Change it under "Einstellungen", section "Gerät" (device): letters, digits and hyphen, 1 to 24 characters. A new name takes effect at once for the `.local` address. The Windows program can change it as well.

## Clock

The clock comes from the PC. When no data from the PC has arrived for 15 minutes, SysopTV uses internet time (pool.ntp.org) with the time zone of Germany, including daylight saving time.

## What the cube shows without a PC

After a start the cube shows "Bereit" with its IP address. If no data arrives within 120 seconds, it shows the display without PC: the clock, "NO CARRIER" and its IP address small at the bottom. The same happens during operation when no data comes for 10 minutes. As soon as data arrives, the chosen design appears.

## Updates later

1. Download the new `sysoptv-<version>.bin`.
2. Open `http://<address of the cube>/update`, or click "Update" at the top of the web interface. The browser asks for the admin password.
3. Choose the file under "Firmware aktualisieren" (update firmware) and press "Hochladen" (upload).
4. Do not cut the power during the update. The cube shows the progress and restarts.

If an update fails, the current firmware stays intact and SysopTV restarts after a few seconds. The update page works in every mode, also in the setup hotspot and in rescue mode, see [Troubleshooting](troubleshooting.md#rescue-mode).

Before you ever go back to the original firmware, download the flash backup, see [Troubleshooting](troubleshooting.md#back-to-the-original-firmware).

Next: [install the Windows app](windows-app.md).
