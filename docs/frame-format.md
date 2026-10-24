# SysopTV picture format "SYF1"

Part of the [developer documentation](developers.md#interfaces-of-the-cube). Back to the [overview of all guides](README.md).

Firmware 0.4.0 and newer can show pictures that a PC draws. The PC sends a picture or a part of it with `POST /api/frame`. The cube decodes it while it is being received and draws it line by line, so it never holds the whole picture in memory. Since firmware 0.5.0 pictures are the only way to show something on the cube: `/api/usage` is gone, and the cube has no designs of its own.

## Request

- Method and path: `POST /api/frame`
- Body: `multipart/form-data` with exactly one file part (any field name, for example `frame`). The file content is the SYF1 data described below. The web server of the cube can only stream file uploads, which is why the picture comes as a file.
- Header `X-SysopTV-Key`: needed only if an API key is set on the cube, the same as for `/api/nocarrier`.
- Header `X-SysopTV-Source` (optional, since firmware 0.4.2): a short name of what the picture shows, for example `Claude Limits: DOS`, `Shopify: Großanzeige Kassenbon` or `WooCommerce: Tabelle`. Non-ASCII characters are sent percent encoded as UTF-8 (`%C3%A4` for ä), the cube decodes them. At most 32 bytes are kept. The overview page of the cube shows it as "Bildmodus: <name>, letztes Bild vor 12 s", and `/api/info` returns it as `frameSource` together with `frameAgeSeconds` (both `null` before the first picture).
- Answers: `200 {"ok":true}`, `400 {"ok":false,"error":"..."}` for broken data, `401` for a missing or wrong key, `409 {"ok":false,"error":"full_frame_required"}` for a part while the screen is not a complete picture (since firmware 0.4.3, see "Valid picture"), `503` in rescue mode.

## Data

All numbers are little endian.

| Offset | Size | Content |
|---|---|---|
| 0 | 4 | Magic `SYF1` (bytes `53 59 46 31`) |
| 4 | 2 | x, left edge of the part, 0 to 239 |
| 6 | 2 | y, top edge of the part, 0 to 239 |
| 8 | 2 | width, at least 1, x + width at most 240 |
| 10 | 2 | height, at least 1, y + height at most 240 |
| 12 | ... | pixels as RLE packets |

The pixels follow row by row from top left to bottom right of the part, exactly width × height pixels. Each pixel is a colour in RGB565 (5 bits red, 6 bits green, 5 bits blue) stored as 2 bytes, low byte first.

The pixels are packed into packets. Each packet starts with one control byte `c`:

- `c` from `0x80` to `0xFF` (top bit set): a **run**. `(c & 0x7F) + 1` pixels (1 to 128) all have the same colour. One colour follows (2 bytes).
- `c` from `0x00` to `0x7F` (top bit clear): a **literal**. `c + 1` pixels (1 to 128) follow, each as its own colour (2 bytes each).

A packet may go on into the next line. After the last pixel no more bytes may follow.

## Errors

The cube answers `400` without crashing, and stops drawing, if

- the magic is wrong or the header is shorter than 12 bytes,
- the part lies outside 240 × 240 or has a width or height of 0,
- more or fewer than width × height pixels arrive, or bytes follow the last pixel.

Lines that were already complete before the error stay on the display.

## Behaviour

- As soon as a picture has arrived completely, the cube is in picture mode and shows only the pictures.
- If no picture arrives for 3 minutes, it shows NO CARRIER: a big clock (internet time), "NO CARRIER", when the last picture came and its IP address (firmware 0.5.0). Firmware 0.4.x goes back to its own display instead.
- Brightness and night mode also apply in picture mode.

## Ending picture mode at once

Since firmware 0.5.0 a PC can switch the cube to NO CARRIER without waiting 3 minutes, for example when the program ends or the PC goes to sleep.

- Method and path: `POST /api/nocarrier`
- Body: none (anything sent is ignored).
- Header `X-SysopTV-Key`: needed only if an API key is set on the cube, the same as for `/api/frame`.
- Answers: `200 {"ok":true}`, `401` for a missing or wrong key, `503` in rescue mode. Firmware 0.4.x answers `404`.
- Afterwards the picture is no longer valid (see below): the next picture must be a full one, a part gets `409` and `full_frame_required`.
- `GET /api/info` lists `nocarrier` under `capabilities` when the cube knows this address.

## Valid picture

Since firmware 0.4.3 the cube remembers whether the whole screen currently shows what the PC drew ("Bild gültig"). `GET /api/info` returns it as `frameValid` (`true` or `false`).

- After a start the picture is never valid.
- It becomes invalid as soon as the firmware draws by itself: NO CARRIER (after 3 minutes without a picture or after `POST /api/nocarrier`), the update screens and every system screen (start, WLAN, setup hotspot, password reset). With firmware 0.4.x also its own design. A picture that breaks off after some lines were drawn makes it invalid as well.
- A complete full picture (x = 0, y = 0, width = 240, height = 240) is always accepted and makes it valid again.
- A part of the screen is only accepted while the picture is valid. Otherwise the cube refuses it before drawing anything, with `409` and `{"ok":false,"error":"full_frame_required"}`. A sender then sends the full picture at once.

This keeps a part from landing on top of something the cube drew itself, which would leave a mixed picture.

## Example

A 2 × 1 part at x = 10, y = 20 with one red and one blue pixel, as one literal packet:

```
53 59 46 31   0A 00   14 00   02 00   01 00   01   00 F8   1F 00
"SYF1"        x=10    y=20    w=2     h=1     lit  red    blue
```

The same part in one colour (two black pixels) as a run: `... 81 00 00`.
