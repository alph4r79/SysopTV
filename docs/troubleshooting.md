# Troubleshooting

This page helps when something does not work as expected. Each section names what you see and what to do.

You need:

- the Windows program, see [Windows app](windows-app.md)
- the address of your cube, see [Installation](installation.md)

## The program does not find the cube

- The cube and the PC must be in the same network. The cube only uses WLAN with 2.4 GHz.
- "Suchen" (search) on the page "Würfel" first tries `sysoptv.local`, then all addresses of your network. A cube in another network, for example a guest network, cannot be found.
- Add the cube by hand with "Von Hand hinzufügen" and its IP address, then press "Verbindung testen" (test connection). "Keine Antwort von SysopTV" (no answer) means the address is wrong or the cube is not reachable.
- The display of the cube shows its IP address after a start ("Bereit") and on the display without PC.
- Give the cube a fixed IP address in your router, so the address does not change.

## The cube shows NO CARRIER

The cube shows a big clock, "NO CARRIER" and below it "letztes Bild HH:MM" (last picture) or "noch kein Bild" (no picture yet), and its IP address. Since firmware 0.5.0 that is the only thing the cube draws itself. It does that:

- 120 seconds after a start without any picture from the program,
- 3 minutes after the last picture,
- at once when the program ends, Windows logs off or shuts down, or the PC goes to sleep.

What to check:

- Is the program running? Its icon must be in the notification area.
- Is the cube reachable? The page "Übersicht" of the program shows the state of every cube.
- If an API key is set in the cube, the same key must be entered in the program, see [Privacy and security](privacy-security.md#api-key-of-the-cube).
- The clock shows `--:--`: the cube has no internet time yet (pool.ntp.org). The night mode needs it as well.

A cube with firmware up to 0.4.x shows its own display instead after 3 minutes. Update it with "Firmware aktualisieren" on the page "Würfel", see [Windows app](windows-app.md#firmware-update).

## The web interface of the cube says "Noch kein Bild vom PC"

The cube got no picture since its start. Check that the program runs, that the cube is in its list with the right address, and that the page "Übersicht" of the program shows "ok" for it.

## The cube shows "Keine Daten von Anthropic"

This is a picture of the program, in the colours of the design. It appears when the program cannot reach Anthropic: after 3 failed queries in a row, or when the last valid values are older than 15 minutes. Look at the page "Claude" of the program for a pause after error 429 or an expired login.

## Error 429 (too many requests)

Anthropic asks to wait. The program then waits at least 5 minutes, or longer if the server asks for it, and keeps sending the last values. The page "Claude" shows the pause under "Sperre" with the time left. Do not set the interval below 120 seconds, and make sure only one sender runs, see below. Details in [Claude limits](claude.md#how-often-the-program-asks).

## Anmeldung abgelaufen (login expired)

The login of Claude Code has run out. Use Claude Code for a moment, then the values come back with the next query. The program never renews the login itself, see [Claude limits](claude.md#anmeldung-abgelaufen-login-expired).

## Altes PowerShell-Skript sendet parallel (the old script sends as well)

The old sender `sysoptv-sender.ps1` runs next to the program and asks for the limits a second time. That can lead to error 429. The program offers to move its shortcut out of the startup folder. End a running script in the Task Manager (powershell.exe) or restart the PC. The program checks every 10 minutes and shows the hint in the status line until the script is gone.

## The PowerShell script gets error 404

Since firmware 0.5.0 the cube takes no numbers any more, only pictures. The old sender `sysoptv-sender.ps1` sends numbers to `/api/usage`, which no longer exists. Use the Windows program instead, see [Windows app](windows-app.md).

## "Die Einstellungen waren beschädigt und wurden aus der Sicherung wiederhergestellt"

The program could not read its `config.json` at the start (for example after a crash of the PC while saving) and took the backup `config.bak.json` from before the last save. Check your cubes and settings once. The damaged file stays as `config.broken.json` next to it. If the backup was damaged as well, the program starts with the defaults, see [Windows app](windows-app.md#where-the-program-keeps-its-files).

## "Die Prüfsumme ... stimmt nicht" or "Im Release fehlt SHA256SUMS.txt"

A file from GitHub (setup or firmware) did not match the checksum in `SHA256SUMS.txt` of the release, or the release has no such file. The program then uses nothing and deletes the download. Try again later; the firmware that comes with the program still works. If it stays like that, download the files by hand from the release page.

## "GitHub ist nicht erreichbar"

"Jetzt nach Updates suchen" got no answer from GitHub. Check the internet connection. The daily check stays silent in that case and tries again an hour later.

## The program does not start again after "Jetzt aktualisieren"

Start it from the start menu. Your settings stay in `config.json`. If Windows blocked the setup, download `sysoptv-app-<version>-setup.exe` from the release page and run it yourself.

## A firmware update aborted

See [When an update aborts](installation.md#when-an-update-aborts).

## Forgotten password

Unplug the cube three times in a row, each time within 10 seconds after plugging it in. On the fourth start the admin password, the API key and all settings are reset. The WLAN stays. The display shows "Passwort zurückgesetzt" (password reset) for 3 seconds. Only starts after plugging in count, crashes and restarts do not.

Then set a new admin password, see [Installation](installation.md#4-set-the-admin-password), and enter it in the program.

## Rescue mode

If the firmware crashes three times in a row, SysopTV starts in rescue mode. The start screen says "RETTUNGSMODUS". It ignores the settings and only offers WLAN, the overview and the update page, so you can install a working firmware, see [When an update aborts](installation.md#when-an-update-aborts). The update page asks for the admin password, if it can still be read. After 60 seconds without a crash the counter starts again.

If the update page cannot be reached at all, the only way back is a USB serial adapter: open the case (two screws at the bottom), solder the adapter on and hold GPIO0 to ground while switching on.

## Windows warns about an unknown publisher

The program is not signed. Click "Weitere Informationen" (more info), then "Trotzdem ausführen" (run anyway), see [Windows app](windows-app.md#1-download-and-install).

## Shop problems

See the troubleshooting sections of [Shopify](shopify-setup.md#troubleshooting) and [WooCommerce](woocommerce-setup.md#troubleshooting).

## Back to the original firmware

Going back to the original GeekMagic firmware has not been tested yet, so there are no instructions for it yet.

Before you try anything in that direction, download the flash backup: on the page "Übersicht" of the cube under "Wartung" (maintenance) click "Flash-Sicherung herunterladen" (download flash backup). It needs an admin password. It is a copy of the flash area 0x100000 to 0x3FA000, where the original firmware keeps its files, including its own web interface. It may contain login data of the original firmware, so keep it to yourself.

The original firmware is available from the [GeekMagic firmware repository](https://github.com/GeekMagicClock/smalltv-ultra).
