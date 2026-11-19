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
| **1.6** | Use the Hallow accent (theme.json, live), crash reports kept on this PC with a notice next start and lastCrash in status; FFmpeg never left running |
| **1.7** | Highlight reel (copy or one GPU re-encode, crossfades, title card), clip titles and thumbnail picker, GIF and 9:16 phone exports |

## Next
### 1.8 - Updates, screenshots and markers
- Updates itself from GitHub (asks first, checks the SHA-256, never touches FFmpeg)
- Screenshot hotkey (Ctrl+Alt+P) and a marker hotkey (Ctrl+Alt+B) - markers become chapters in clips and sessions

### 1.9 - Separate sound per app
- Game, Discord (or other apps you pick) on their own tracks with Windows' per-app capture, next to everything + mic; falls back to today's capture on older Windows
- Choose which sound tracks go into exports (Discord copy, phone version, highlight reel)

### 1.10 - Webcam and memory buffer
- Optional webcam overlay (corner and size)
- Replay buffer in memory instead of on disk (optional), to spare the SSD

### Gaps vs ShadowPlay / Medal
What NVIDIA ShadowPlay (NVIDIA app), Medal.tv, Outplayed and OBS's replay buffer do that Clips didn't (as of 1.7):
- Updates itself (NVIDIA app, Medal, Outplayed, OBS) - **buildable**: GitHub releases with checksums, like Hallow Hub (1.8)
- Screenshot hotkey (ShadowPlay Alt+F1, Medal, OBS) - **buildable** (1.8)
- Bookmarks / markers while recording (Outplayed, Medal, OBS chapter markers) - **buildable**: MP4 chapters (1.8)
- Separate audio per app, e.g. game and Discord on their own tracks (Medal, OBS application audio capture, ShadowPlay "separate both tracks") - **buildable**: Windows 10 2004+ per-process loopback (1.9)
- Pick the sound tracks when exporting (Medal editor) - **buildable** (1.9)
- Webcam overlay (Medal, OBS, ShadowPlay camera) - **buildable**: FFmpeg DirectShow input (1.10)
- Replay buffer in memory (OBS) - **buildable**: segments handed over pipes instead of files (1.10)
- Window-only / game capture instead of the whole screen (OBS game capture) - **not now**: OBS hooks into the game (anti-cheat risk); Windows' own window capture needs a newer FFmpeg filter that isn't tested here
- HDR capture with tone mapping (ShadowPlay, OBS) - **not now**: needs an HDR screen to test; there isn't one here
- Hosted share links, cloud library, social feed (Medal, Outplayed) - **not buildable**: needs a server, accounts and storage
- Upload to YouTube / TikTok (Medal, ShadowPlay) - **not buildable here**: needs a registered app with OAuth secrets; Clips makes the files (Discord copy, phone version) and Hallow Toolkit posts to Discord
- Automatic clips from game events / AI highlights (Outplayed, Medal, ShadowPlay Highlights) - **not buildable here**: only a few games (CS2, Dota 2) expose events and none played here do (see Later)
- Live streaming (OBS, ShadowPlay) - **not planned**: a different kind of app
- In-game overlay with performance stats (ShadowPlay Alt+Z) - **covered** by Hallow Overlay
## Later
- Auto clips from game events - only a few games (CS2, Dota 2) expose them, and none of the games played here do
