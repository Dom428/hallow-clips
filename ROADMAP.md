# Hallow Clips roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | Instant replay: Ctrl+Alt+S saves the last 30 s (or your length) as an MP4; records only while a game runs; encoded on the graphics card (NVIDIA, AMD, Intel or Windows); game sound and your mic on separate tracks; clip list with thumbnails; FFmpeg set up with one click and checked |
| **1.1** | Sound locked to the clock over long sessions, FFmpeg stops with the app, Ctrl+Alt+S taken warning, recorder.log; Hallow family link (pipe, --cmd, status.json, Clip saved in Overlay) |
| **1.2** | Trim without re-encoding (keyframe cut), Copy for Discord (under 10 MB, on the clipboard), rename, favourites |
| **1.3** | Record a whole session (Ctrl+Alt+R), HEVC / AV1 when the card can (one-frame test encode), space limit with oldest non-favourites to the Recycle Bin (off by default) |
| **1.4** | Hallow family: save a clip from Hallow Remote, Clip saved in Hallow Overlay, this game's clips on its Hallow Hub page, opt-in Discord post through Hallow Toolkit (status.json with real game names and install folders) |
| **1.5** | One Hallow icon: open / menu / invoke / quit for Hallow Hub's tray menu, Use the Hallow icon (own icon hidden while Hub hosts), balloons through Hub |

## Next

### 1.6 - Hallow accent and crash reports
- Use the Hallow accent: the colour picked in Hallow Toolkit (theme.json) replaces Clips' orange, live (on by default)
- Crash reports kept on this PC (crashes folder, newest 20), a notice on the next start, lastCrash in status; FFmpeg never left running

### 1.7 - Highlight reel, titles and exports
- Highlight reel: pick and order clips, join them into one MP4 (copy when formats match, else one GPU re-encode), optional crossfade and title card
- Clip titles (MP4 title metadata, shown in the list, in status.json) and pick the thumbnail frame
- Export a GIF (palette, length cap, width choices) or a 9:16 phone version under ~50 MB

## Later
- Auto clips from game events - only a few games (CS2, Dota 2) expose them, and none of the games played here do
