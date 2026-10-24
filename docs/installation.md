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

- under "Anzeige" (display) what the cube shows: "Bildmodus" (picture mode) with the name of the picture and its age, "NO CARRIER" with the age of the last picture, or "Noch kein Bild vom PC" (no picture from the PC yet)
- the device: WLAN, IP address, mDNS name, signal, whether the clock is set, uptime, free memory, reason of the last restart
- under "Wartung" (maintenance) the link "Flash-Sicherung herunterladen" (download flash backup)

The values refresh every 10 seconds.

**Einstellungen** (`/settings`):

- brightness 10 to 100 %
- night mode with a start and an end time and its own brightness from 0 to 100 % (0 switches the display off). It needs the time from the internet.
- device name, see below
- API key: "Neuen Schlüssel erzeugen" (create new key) and "Schlüssel löschen" (delete key), see [Privacy and security](privacy-security.md#api-key-of-the-cube)
- change the admin password

Press "Speichern" (save). The settings stay in the cube and are only written when something changed. The Windows program can change them as well, see [Windows app](windows-app.md#einstellungen-im-würfel-settings-in-the-cube).

What the cube shows comes from the Windows program, which draws every picture. Since firmware 0.5.0 the cube has no designs of its own. Settings for designs from firmware 0.4.x are ignored after the update.

**Update** (`/update`), see below.

## Device name

Every cube has a device name, so several cubes can run in one network. The default is `sysoptv-` plus the last four hex digits of the chip ID, for example `sysoptv-3fa2`. The cube is then reachable at `http://sysoptv-3fa2.local`, and its setup hotspot is called `SysopTV-Setup-3fa2`.

Change it under "Einstellungen", section "Gerät" (device): letters, digits and hyphen, 1 to 24 characters. A new name takes effect at once for the `.local` address. The Windows program can change it as well.

## Clock

The clock of the cube comes from internet time (pool.ntp.org) with the time zone of Germany, including daylight saving time. It is shown on the NO CARRIER screen and used for the night mode. Without internet the clock shows `--:--`.

## What the cube shows without a PC

After a start the cube shows "Bereit" with its IP address until the first picture from the program arrives. Without a picture it shows NO CARRIER:

- 120 seconds after the start if no picture came at all
- 3 minutes after the last picture
- at once when the program ends, Windows logs off or shuts down, or the PC goes to sleep: the program tells the cube

NO CARRIER shows the clock big, below it "NO CARRIER", when the last picture came ("letztes Bild 14:05", or "noch kein Bild") and the IP address small at the bottom. As soon as pictures come again, the cube shows them.

## Updates later

**From the Windows program.** The program brings the matching firmware with it. With the update check on (page "Programm"), it also takes a newer firmware from the latest release on GitHub, checked against `SHA256SUMS.txt`. If a cube runs an older SysopTV firmware, the page "Würfel" shows the button "Firmware aktualisieren" below the list, see [Windows app](windows-app.md#firmware-update). This is the easiest way.

**In the browser:**

1. Download the new `sysoptv-<version>.bin`, or take it from the folder `firmware` of the installed program (`%LOCALAPPDATA%\Programs\SysopTV\firmware`).
2. Open `http://<address of the cube>/update`, or click "Update" at the top of the web interface. The browser asks for the admin password.
3. Choose the file under "Firmware aktualisieren" (update firmware) and press "Hochladen" (upload).
4. Do not cut the power during the update. The cube shows the progress and restarts.

The update page works in every mode, also in the setup hotspot and in rescue mode.

## When an update aborts

The cube checks password and form before it writes anything, and it writes the new firmware next to the old one. Only a complete file replaces the old firmware.

1. **The program or the page reports an error** ("Update abgebrochen", "Update fehlgeschlagen"): the old firmware stays. The cube shows "Update fehlgeschlagen" and restarts after a few seconds. Simply try again.
2. **The connection broke off** (WLAN gone, PC went to sleep): the old firmware keeps running. Try again.
3. **The cube does not come back after the update:** cut the power for a moment and plug it in again. If it then shows "RETTUNGSMODUS" (rescue mode, after three crashes in a row) or works normally with the old version, open `http://<address of the cube>/update` in the browser and upload the file once more. In rescue mode only WLAN and the update page run; the password is the admin password, if it can still be read, otherwise the page is open.
4. **The cube cannot find its WLAN:** it opens the hotspot `SysopTV-Setup-xxxx`. Connect to it; the update page is at `http://192.168.4.1/update` there as well.
5. **Nothing works any more** and the update page cannot be reached at all: only a USB serial adapter helps, see [Troubleshooting](troubleshooting.md#rescue-mode).

Before you ever go back to the original firmware, download the flash backup, see [Troubleshooting](troubleshooting.md#back-to-the-original-firmware).

Next: [install the Windows app](windows-app.md).
