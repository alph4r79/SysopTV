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

## The cube suddenly shows another picture

The cube shows its own design, the **Ersatzanzeige, wenn der PC nichts schickt** (fall back display), when no pictures come from the program for a few minutes. When the program stops sending, for example because the PC is off or the program was closed, the cube keeps the last picture for up to 3 minutes, then it switches.

- Is the program running? Its icon must be in the notification area.
- Is the cube reachable? The page "Übersicht" of the program shows the state of every cube.
- A cube with Claude Limits and firmware 0.4.2 or newer shows the last numbers in its fall back display. Other cubes show the clock and NO CARRIER.

You choose the fall back display on the page "Würfel" under "Einstellungen im Würfel", see [Windows app](windows-app.md#einstellungen-im-würfel-settings-in-the-cube).

## The web interface of the cube says "Keine Daten vom PC"

The cube got neither pictures nor numbers from the PC for a while. Check that the program runs, that the cube is in its list with the right address, and that the page "Übersicht" of the program shows "ok" for it. If an API key is set in the cube, the same key must be entered in the program, see [Privacy and security](privacy-security.md#api-key-of-the-cube).

## The cube shows NO CARRIER

The cube shows a big clock, "NO CARRIER" and below it "letzte Daten HH:MM" (last data) or "noch keine Daten" (no data yet), and its IP address. It does that:

- 120 seconds after a start without any data from the PC,
- when no data came for 10 minutes,
- when the PC reports that it cannot reach Anthropic: after 3 failed queries in a row, or when the last valid values are older than 15 minutes.

Check that the program runs, and look at the page "Claude" of the program for a pause after error 429 or an expired login.

## Error 429 (too many requests)

Anthropic asks to wait. The program then waits at least 5 minutes, or longer if the server asks for it, and keeps sending the last values. The page "Claude" shows the pause under "Sperre" with the time left. Do not set the interval below 120 seconds, and make sure only one sender runs, see below. Details in [Claude limits](claude.md#how-often-the-program-asks).

## Anmeldung abgelaufen (login expired)

The login of Claude Code has run out. Use Claude Code for a moment, then the values come back with the next query. The program never renews the login itself, see [Claude limits](claude.md#anmeldung-abgelaufen-login-expired).

## Altes PowerShell-Skript sendet parallel (the old script sends as well)

The old sender `sysoptv-sender.ps1` runs next to the program and asks for the limits a second time. That can lead to error 429. The program offers to move its shortcut out of the startup folder. End a running script in the Task Manager (powershell.exe) or restart the PC. The program checks every 10 minutes and shows the hint in the status line until the script is gone.

## Forgotten password

Unplug the cube three times in a row, each time within 10 seconds after plugging it in. On the fourth start the admin password, the API key and all settings are reset. The WLAN stays. The display shows "Passwort zurückgesetzt" (password reset) for 3 seconds. Only starts after plugging in count, crashes and restarts do not.

Then set a new admin password, see [Installation](installation.md#4-set-the-admin-password), and enter it in the program.

## Rescue mode

If the firmware crashes three times in a row, SysopTV starts in rescue mode. The start screen says "RETTUNGSMODUS". It ignores the settings and only offers WLAN, the overview and the update page, so you can install a working firmware. The update page asks for the admin password, if it can still be read. After 60 seconds without a crash the counter starts again.

## Windows warns about an unknown publisher

The program is not signed. Click "Weitere Informationen" (more info), then "Trotzdem ausführen" (run anyway), see [Windows app](windows-app.md#1-download-and-install).

## Shop problems

See the troubleshooting sections of [Shopify](shopify-setup.md#troubleshooting) and [WooCommerce](woocommerce-setup.md#troubleshooting).

## Back to the original firmware

Going back to the original GeekMagic firmware has not been tested yet, so there are no instructions for it yet.

Before you try anything in that direction, download the flash backup: on the page "Übersicht" of the cube under "Wartung" (maintenance) click "Flash-Sicherung herunterladen" (download flash backup). It needs an admin password. It is a copy of the flash area 0x100000 to 0x3FA000, where the original firmware keeps its files, including its own web interface. It may contain login data of the original firmware, so keep it to yourself.

The original firmware is available from the [GeekMagic firmware repository](https://github.com/GeekMagicClock/smalltv-ultra).
