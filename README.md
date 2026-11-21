# Hallow Clips

Instant replay for your games - part of the Hallow family with [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Vault](https://github.com/Dom428/hallow-vault), [Hallow Overlay](https://github.com/Dom428/hallow-overlay), [Hallow Remote](https://github.com/Dom428/hallow-remote) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit).

- Keeps the last moments of your game in a rolling buffer while a game runs; **Ctrl+Alt+S** saves them as an MP4
- Screen grabbed with Desktop Duplication and encoded on the graphics card (NVENC / AMF / Quick Sync)
- Game sound (WASAPI loopback) and your mic on **separate audio tracks**; on Windows 10 2004+ also the **game, Discord, Spotify, browser or TeamSpeak on their own tracks** (per-process loopback), and a choice of which tracks go into exports
- 15 s to 2 min, 30 / 60 fps, quality levels, any screen; clip list with thumbnails
- **Highlight reel**: pick and order clips, joined with `-c copy` when the formats match, otherwise one GPU re-encode; optional crossfades and title card
- Clip titles (MP4 title metadata, in the list and `status.json`), pick the thumbnail frame, GIF export (palettegen/paletteuse, up to 15 s, 320-640 px) and a 9:16 phone version under 50 MB
- Optional **webcam overlay** (corner, size) and a **replay buffer in memory** (FFmpeg writes each 2 s piece into a pipe held in RAM - nothing on the SSD); both fall back by themselves if they fail
- **Ctrl+Alt+P** screenshot (PNG in the game's Screenshots folder), **Ctrl+Alt+B** marker - markers become MP4 chapters and jump points in Trim
- Updates itself from GitHub releases (asks first, checks the SHA-256; FFmpeg is never part of an update)
- **Ctrl+Alt+R** records a whole session (start / stop) next to the replay buffer
- **Steps aside for Discord Clips** (1.11): while Discord's own Clips (`discord_clips.exe`) records the same game, two hardware encoders would run at once - Clips pauses its replay buffer instead (default) and starts again by itself a few seconds after Discord Clips stops. Recording is told by the process and, where Windows has them, its GPU Engine *VideoEncode* counters (an idle Discord Clips doesn't count). Settings: *When Discord Clips is also recording* - *Let Discord record - pause Hallow Clips* / *Keep both recording*; *Record anyway this session* in the menu and the main window lasts until the game closes. Ctrl+Alt+S then says Discord Clips is recording.
- H.264, or HEVC / AV1 for smaller files when FFmpeg and the graphics card can (checked with a one-frame test encode)
- Optional space limit for the clips folder: the oldest non-favourites go to the Recycle Bin
- **Editor** (1.13, the scissors): a picture strip along the timeline, start / end with frame steps, *Fast* (cut at keyframes, no re-encoding) or *Exact* (any frame); **cut out a part** in the middle, **slow motion** (0.5x / 0.25x, sound slowed with it) or 2x for a part, and **blur a box** (names, chat, kill feed - pick a corner or drag it on the picture; Gaussian blur). Anything past a plain trim is one re-encode on the graphics card; the original clip always stays
- Trim clips without re-encoding, **Copy for Discord** (re-encoded under 10, 25 or 50 MB - variable bitrate, up to 1440p60 when there's room - and put on the clipboard), rename, favourites
- **Clip library** (1.12): search by title, file name or game (accents and case ignored), filter by game, date (today / 7 days / 30 days) and favourites, *Show more* for older clips; **Select** several clips and move them to the Recycle Bin together (an in-app question first, with the total size and any favourites named); **Storage** shows the space each game's folder uses (clips, sessions, GIFs, screenshots) and the free space on the drive - click a game to see only its clips
- **Light and dark mode** (1.14): follows `mode` in `%APPDATA%\Hallow\theme.json` (Hallow Toolkit: *Light* / *Dark* / *Same as Windows*, which follows Windows' app setting live); Settings > *Look*: *Follow Hallow* (default), *Light* or *Dark* just for Clips. Every window, menu, tooltip and the title bar follow; accent text is darkened in light mode so it stays readable (4.5:1)
- Sound locked to the clock, so it stays in step with the picture however long you play
- Hallow family link: other Hallow apps save a clip and list your clips (see below)

Uses [FFmpeg](https://ffmpeg.org) (LGPL build from BtbN/FFmpeg-Builds), downloaded on first use and checked against GitHub's SHA-256 - not included here.

## Hallow family link

- Named pipe `HallowFamily.Clips.<user>` (this Windows user only, never over the network): one line in, one line out. `ping`, `status`, `save` (replies `ok {"file":"..."}` or `error Nothing recorded yet`).
- `HallowClips.exe --cmd "save"` sends a command to the running copy and prints the reply (exit code 0 ok, 1 error, 2 not running).
- `%LOCALAPPDATA%\HallowClips\status.json`: recording state, current game (name and install folder), clip length, folder and the 30 newest clips (file, game, the game's install folder, time, size, length, thumbnail, favourite). Written atomically when something changes.
- After a save, Hallow Overlay shows *Clip saved* when it runs.
- v1.5 One Hallow icon: `open`, `menu` (Save clip, Record session, Replay buffer, Open clips folder), `invoke <id>`, `quit`. With *Use the Hallow icon* on (default), Clips asks Hub `tray-host` every 5 s and hides its own tray icon while Hub hosts the family icon; balloons then go through Hub `notify`. `status.json` has `trayHosted`.
- v1.12: `storage` (`ok {"folder","totalBytes","freeBytes","games":[{"game","bytes","clips"}]}`, biggest first) and `find <words>` (`ok {"query","clips":[{"file","game","title","time","sizeBytes"}]}`, newest first, at most 50; every word must match the title, file name or game).
- v1.11: `status` / `status.json` have `pausedFor` (`"Discord Clips"` while Clips stands aside for it, otherwise `null`); `menu` adds `anyway` (*Record anyway this session*, ticked when on) while Discord Clips records the game; `save` then replies `error Discord Clips is recording this game - Hallow Clips is paused`.
- v1.6: *Use the Hallow accent* (default on) takes the accent from `%APPDATA%\Hallow\theme.json` (Hallow Toolkit) and follows it live. Crash reports go to `%LOCALAPPDATA%\HallowClips\crashes` (newest 20, never sent); `status` has `lastCrash`.
- Used by: Hallow Remote (save a clip from your phone), Hallow Overlay (*Clip saved*), Hallow Hub (this game's clips on its page), Hallow Toolkit (opt-in Discord post of new clips - the webhook stays in Toolkit).

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## Credits

FFmpeg by the FFmpeg developers (LGPL). Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).