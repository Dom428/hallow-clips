# Hallow Clips roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Instant replay: Ctrl+Alt+S saves the last 30 s (or your length) as an MP4; records only while a game runs; encoded on the graphics card (NVIDIA, AMD, Intel or Windows); game sound and your mic on separate tracks; clip list with thumbnails; FFmpeg set up with one click and checked |
| **1.1** | Sound locked to the clock over long sessions, FFmpeg stops with the app, Ctrl+Alt+S taken warning, recorder.log; Hallow family link (pipe, --cmd, status.json, Clip saved in Overlay) |
| **1.2** | Trim without re-encoding (keyframe cut), Copy for Discord (under 10 MB, on the clipboard), rename, favourites |
| **1.3** | Record a whole session (Ctrl+Alt+R), HEVC / AV1 when the card can (one-frame test encode), space limit with oldest non-favourites to the Recycle Bin (off by default) |
| **1.4** | Hallow family: save a clip from Hallow Remote, Clip saved in Hallow Overlay, this game's clips on its Hallow Hub page, opt-in Discord post through Hallow Toolkit (status.json with real game names and install folders) |

## Next

### 1.5 - One Hallow icon
- Hallow Hub hosts one tray icon for the family: Clips answers open, menu (Save clip, Record session, Replay buffer, Open clips folder), invoke and quit on its pipe
- Use the Hallow icon setting (on by default): Clips' own icon hides while Hub hosts, and comes back when Hub doesn't
- Tray balloons go through Hub's icon while Clips' icon is hidden
