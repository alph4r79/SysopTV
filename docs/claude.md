# Claude limits

After reading this you know where the numbers on the cube come from, what they mean, and why the program asks only every two minutes.

You need:

- Claude Code installed and logged in with a Claude subscription. The file `.credentials.json` must exist in the folder `.claude` of your Windows user profile.
- the Windows program with a cube set to the function Claude Limits, see [Windows app](windows-app.md)

## Where the numbers come from

The cube cannot fetch the limits by itself. The program on your PC reads the login of Claude Code from `%USERPROFILE%\.claude\.credentials.json`, asks Anthropic for your usage limits and sends only the percentages and reset times to the cube over your local network.

The query uses an undocumented endpoint that Claude Code uses as well (the same as [CodexBar](https://github.com/steipete/CodexBar)). It is not an official interface. It can change or disappear at any time, and then the cube shows no new values.

The limits belong to your whole Claude account, so they also count your use of claude.ai and the Claude app.

## What the cube shows

- **Session**: your 5 hour session limit in percent, with the time until it resets as a countdown, for example "noch 2:13" (2 hours 13 minutes left).
- **Woche** (week): your weekly limit in percent, with the reset day and time, for example "Do 23:00", "heute 23:00" (today) or "morgen 23:00" (tomorrow).
- **verbraucht** or **übrig**: each cube shows the percentages as used (like on claude.ai) or as remaining. You choose this per cube under "Prozent zeigen".
- **Warning colour**: when less than 20 % are left, the values change their colour (in 4-Ton-Grün they blink instead).
- **nicht aktiv** (not active): no session is running at the moment, so there is no reset time. It starts again with your next use of Claude.
- **Stand HH:MM** (as of): the values are not fresh. They are shown with the time they were read.

## Anmeldung abgelaufen (login expired)

The login of Claude Code runs out after a few hours. It is only renewed when you use Claude Code. When it has run out, the cube shows "ANMELDUNG ABGELAUFEN" and "Claude Code starten", the icon turns amber and the page Claude says "abgelaufen, Claude Code kurz benutzen".

Use Claude Code for a moment, for example send it a short question. With the next query the values come back.

## How often the program asks

The program asks Anthropic every 120 seconds. You can set a longer interval on the page Claude ("Abfragen alle"), but not a shorter one.

Asking every 60 seconds made the server answer with error 429 (too many requests) all the time. After a 429 the program waits at least 5 minutes, or longer if the server asks for it. The page Claude shows such a pause under "Sperre" with the time left.

"Jetzt abfragen" (query now) asks at once, but never within 120 seconds after the last query. Until then the program sends the last values again.

When a query fails, the display does not change at once. The program keeps sending the last valid values, so the cube knows that the PC is still running. It reports an error only after 3 failed queries in a row, or when the last valid values are older than 15 minutes. The cube then shows "NO CARRIER".

After a restart of the program it waits until the interval has passed and a pause is over, and shows the stored values until then.

## What the program never does

- It never renews the login of Claude Code. Renewing it changes the stored login of Claude Code, and a mistake there could log you out. That is why you use Claude Code for a moment when the login has expired.
- It never sends the login anywhere else than to Anthropic, and never to the cube.
- It never writes the login to its log, its settings or `state.json`.

More in [Privacy and security](privacy-security.md).
