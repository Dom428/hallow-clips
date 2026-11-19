# Hallow Clips

Instant replay for your games - part of the Hallow family with [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Vault](https://github.com/Dom428/hallow-vault), [Hallow Overlay](https://github.com/Dom428/hallow-overlay), [Hallow Remote](https://github.com/Dom428/hallow-remote) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit).

- Keeps the last moments of your game in a rolling buffer while a game runs; **Ctrl+Alt+S** saves them as an MP4
- Screen grabbed with Desktop Duplication and encoded on the graphics card (NVENC / AMF / Quick Sync)
- Game sound (WASAPI loopback) and your mic on **separate audio tracks**; on Windows 10 2004+ also the **game, Discord, Spotify, browser or TeamSpeak on their own tracks** (per-process loopback), and a choice of which tracks go into exports
- 15 s to 2 min, 30 / 60 fps, quality levels, any screen; clip list with thumbnails
- **Highlight reel**: pick and order clips, joined with `-c copy` when the formats match, otherwise one GPU re-encode; optional crossfades and title card
- Clip titles (MP4 title metadata, in the list and `status.json`), pick the thumbnail frame, GIF export (palettegen/paletteuse, up to 15 s, 320-640 px) and a 9:16 phone version under 50 MB
- **Ctrl+Alt+P** screenshot (PNG in the game's Screenshots folder), **Ctrl+Alt+B** marker - markers become MP4 chapters and jump points in Trim
- Updates itself from GitHub releases (asks first, checks the SHA-256; FFmpeg is never part of an update)
- **Ctrl+Alt+R** records a whole session (start / stop) next to the replay buffer
- H.264, or HEVC / AV1 for smaller files when FFmpeg and the graphics card can (checked with a one-frame test encode)
- Optional space limit for the clips folder: the oldest non-favourites go to the Recycle Bin
- Trim clips without re-encoding, **Copy for Discord** (re-encoded under 10 MB and put on the clipboard), rename, favourites
- Sound locked to the clock, so it stays in step with the picture however long you play
- Hallow family link: other Hallow apps save a clip and list your clips (see below)

Uses [FFmpeg](https://ffmpeg.org) (LGPL build from BtbN/FFmpeg-Builds), downloaded on first use and checked against GitHub's SHA-256 - not included here.

## Hallow family link

- Named pipe `HallowFamily.Clips.<user>` (this Windows user only, never over the network): one line in, one line out. `ping`, `status`, `save` (replies `ok {"file":"..."}` or `error Nothing recorded yet`).
- `HallowClips.exe --cmd "save"` sends a command to the running copy and prints the reply (exit code 0 ok, 1 error, 2 not running).
- `%LOCALAPPDATA%\HallowClips\status.json`: recording state, current game (name and install folder), clip length, folder and the 30 newest clips (file, game, the game's install folder, time, size, length, thumbnail, favourite). Written atomically when something changes.
- After a save, Hallow Overlay shows *Clip saved* when it runs.
- v1.5 One Hallow icon: `open`, `menu` (Save clip, Record session, Replay buffer, Open clips folder), `invoke <id>`, `quit`. With *Use the Hallow icon* on (default), Clips asks Hub `tray-host` every 5 s and hides its own tray icon while Hub hosts the family icon; balloons then go through Hub `notify`. `status.json` has `trayHosted`.
- v1.6: *Use the Hallow accent* (default on) takes the accent from `%APPDATA%\Hallow\theme.json` (Hallow Toolkit) and follows it live. Crash reports go to `%LOCALAPPDATA%\HallowClips\crashes` (newest 20, never sent); `status` has `lastCrash`.
- Used by: Hallow Remote (save a clip from your phone), Hallow Overlay (*Clip saved*), Hallow Hub (this game's clips on its page), Hallow Toolkit (opt-in Discord post of new clips - the webhook stays in Toolkit).

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## Credits

FFmpeg by the FFmpeg developers (LGPL). Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).