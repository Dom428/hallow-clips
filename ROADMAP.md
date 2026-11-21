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
| **1.8** | Updates itself from GitHub (asks first, SHA-256, never FFmpeg), Ctrl+Alt+P screenshots, Ctrl+Alt+B markers as chapters |
| **1.9** | Game, Discord and other picked apps on their own sound tracks (per-process loopback, Windows 10 2004+), sound tracks chosen for exports |
| **1.10** | Webcam overlay (corner, size), replay buffer in memory (spares the SSD), settings behind a Settings button |
| **1.11** | Steps aside for Discord Clips (discord_clips.exe running and its GPU video encoder busy): pauses the replay buffer and resumes by itself; setting pause / keep both, Record anyway this session (menu, `anyway`), `pausedFor` in status |
| **1.12** | Clip library: search (title, name, game), filters for game, date and favourites, Show more; Select several clips and move them to the Recycle Bin after an in-app question; storage per game with free space; Copy for Discord under 10 / 25 / 50 MB; pipe `storage` and `find` |
| **1.13** | Editor: picture strip on the timeline, frame-exact trim (re-encoded) next to the lossless keyframe trim, cut out a middle part, slow motion (0.5x / 0.25x) or 2x for a part, blur a box (names, chat) - one re-encode on the graphics card, the original stays |
| **1.14** | Light and dark mode: follows Hallow Toolkit's Light / Dark / Same as Windows (theme.json `mode`, live), Settings > Look (Follow Hallow / Light / Dark) for Clips alone; title bar, menus, tooltips and every window; accent text darkened for contrast; pipe `ping` reports `look` |
| **1.15** | Light mode readability fixes: white slider knobs with an accent ring and an accent track, the Hallow family accent fill / text rule (same as Toolkit 5.16), text amber for warnings, open windows re-theme live; dark unchanged |

## Next
### Gaps vs ShadowPlay / Medal
What NVIDIA ShadowPlay (NVIDIA app), Medal.tv, Outplayed and OBS's replay buffer do that Clips didn't (as of 1.7):
- Updates itself (NVIDIA app, Medal, Outplayed, OBS) - **buildable**: GitHub releases with checksums, like Hallow Hub (1.8)
- Screenshot hotkey (ShadowPlay Alt+F1, Medal, OBS) - **buildable** (1.8)
- Bookmarks / markers while recording (Outplayed, Medal, OBS chapter markers) - **buildable**: MP4 chapters (1.8)
- Separate audio per app, e.g. game and Discord on their own tracks (Medal, OBS application audio capture, ShadowPlay "separate both tracks") - **buildable**: Windows 10 2004+ per-process loopback (1.9)
- Pick the sound tracks when exporting (Medal editor) - **buildable** (1.9)
- Webcam overlay (Medal, OBS, ShadowPlay camera) - **buildable**: FFmpeg DirectShow input (1.10)
- Replay buffer in memory (OBS) - **buildable**: segments handed over pipes instead of files (1.10)
- Search and filter the clip library, delete several clips at once, storage per game (NVIDIA app gallery, Medal library) - **buildable** (1.12)
- Bigger share sizes for Discord (Nitro / boosted servers allow more than 10 MB) - **buildable**: Copy for Discord under 10, 25 or 50 MB (1.12)
- An editor: frame-exact cuts, cut out a middle part, slow motion, blur a name (Medal editor, Outplayed, NVIDIA app) - **buildable**: one FFmpeg graph, re-encoded once (1.13)
- Window-only / game capture instead of the whole screen (OBS game capture) - **not now**: OBS hooks into the game (anti-cheat risk); Windows' own window capture needs a newer FFmpeg filter that isn't tested here
- HDR capture with tone mapping (ShadowPlay, OBS) - **not now**: needs an HDR screen to test; there isn't one here
- Hosted share links, cloud library, social feed (Medal, Outplayed) - **not buildable**: needs a server, accounts and storage
- Upload to YouTube / TikTok (Medal, ShadowPlay) - **not buildable here**: needs a registered app with OAuth secrets; Clips makes the files (Discord copy, phone version) and Hallow Toolkit posts to Discord
- Automatic clips from game events / AI highlights (Outplayed, Medal, ShadowPlay Highlights) - **not buildable here**: only a few games (CS2, Dota 2) expose events and none played here do (see Later). Reading game memory or hooking the game is out (anti-cheat); a loudness-spike guess was looked at again in 1.12 and left out - gunfire, explosions and music are loud whether or not anything worth keeping happened
- Live streaming (OBS, ShadowPlay) - **not planned**: a different kind of app
- In-game overlay with performance stats (ShadowPlay Alt+Z) - **covered** by Hallow Overlay
- Light mode (NVIDIA app, Medal and Outplayed follow Windows) - **buildable**: the Hallow family switch in Hallow Toolkit, or Look in Settings (1.14); readability fixes (1.15)
## Later
- Auto clips from game events - only a few games (CS2, Dota 2) expose them, and none of the games played here do
