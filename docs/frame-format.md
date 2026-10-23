# SysopTV picture format "SYF1"

Firmware 0.4.0 and newer can show pictures that a PC draws. The PC sends a picture or a part of it with `POST /api/frame`. The cube decodes it while it is being received and draws it line by line, so it never holds the whole picture in memory.

## Request

- Method and path: `POST /api/frame`
- Body: `multipart/form-data` with exactly one file part (any field name, for example `frame`). The file content is the SYF1 data described below. The web server of the cube can only stream file uploads, which is why the picture comes as a file.
- Header `X-SysopTV-Key`: needed only if an API key is set on the cube, the same as for `/api/usage`.
- Answers: `200 {"ok":true}`, `400 {"ok":false,"error":"..."}` for broken data, `401` for a missing or wrong key, `503` in rescue mode.

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

- As soon as a picture has arrived completely, the cube is in picture mode: it only shows pictures and does not draw its own designs over them.
- If no picture arrives for 3 minutes, it goes back to its own display without PC (clock or NO CARRIER with the IP address).
- If usage data arrives at `/api/usage` instead, it leaves picture mode at once and draws its own designs again.
- Brightness and night mode also apply in picture mode.

## Example

A 2 × 1 part at x = 10, y = 20 with one red and one blue pixel, as one literal packet:

```
53 59 46 31   0A 00   14 00   02 00   01 00   01   00 F8   1F 00
"SYF1"        x=10    y=20    w=2     h=1     lit  red    blue
```

The same part in one colour (two black pixels) as a run: `... 81 00 00`.
