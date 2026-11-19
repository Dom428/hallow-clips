HALLOW CLIPS - instant replay for your games

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)
4. The first time, click "Set up FFmpeg" - it downloads the free FFmpeg recorder (about 171 MB) from GitHub
   and checks it before use.

WHAT'S NEW IN 1.9
- Own sound tracks: the game and Discord (or Spotify, your browser, TeamSpeak) each on their own track.
- Sound in exports: choose what goes into Discord copies, phone versions and highlight reels.

WHAT'S NEW IN 1.8
- Updates itself: an Update button appears when a new version is out (asks first, never touches FFmpeg).
- Ctrl+Alt+P takes a screenshot; Ctrl+Alt+B marks a moment while recording (chapters in the clip, jump points in Trim).

WHAT'S NEW IN 1.7
- Highlight reel: tick clips, put them in order and join them into one video (crossfades and a title card if you like).
- Clip titles and your own thumbnail picture (the ... button on a clip).
- Export a GIF, or a 9:16 phone version for TikTok, Shorts and Reels.

WHAT'S NEW IN 1.6
- Use the Hallow accent: the colour you pick in Hallow Toolkit replaces Clips' orange, right away.
- If Clips ever crashes, a report stays on this PC (never sent) and you're told next time. Recording always stops with it.

WHAT'S NEW IN 1.5
- One Hallow icon: with Hallow Hub running, Clips is in Hub's tray menu (Save clip, Record session, Replay buffer,
  Open clips folder) instead of having its own icon. Turn off "Use the Hallow icon" to keep your own.

WHAT'S NEW IN 1.4
- Hallow family: save a clip from Hallow Remote on your phone; Hallow Overlay shows Clip saved.
- Your clips of a game on its Hallow Hub page, and an opt-in Discord post of new clips through Hallow Toolkit
  (the webhook stays in Toolkit on this PC).

WHAT'S NEW IN 1.3
- Record session (Ctrl+Alt+R): records everything until you stop, next to the instant replay.
- Video format: HEVC or AV1 for smaller files, offered when your graphics card can.
- Space limit for the clips folder (off unless you pick one): the oldest clips that aren't favourites go to the Recycle Bin.

WHAT'S NEW IN 1.2
- Trim a clip (scissors): no re-encoding, so it's instant and keeps full quality. The original stays.
- Copy for Discord: a copy under 10 MB on the clipboard - paste it into Discord with Ctrl+V.
- Rename clips (pen) and star your favourites.

WHAT'S NEW IN 1.1
- Sound stays locked to the picture however long you play, and recording stops if the app is closed hard.
- Tells you when another app already uses Ctrl+Alt+S.
- Hallow family: Hallow Remote/Toolkit can save a clip, Hallow Hub can list your clips, Hallow Overlay shows Clip saved.
  (HallowClips.exe --cmd "save" works from scripts too.)

HOW IT WORKS
- While a game runs, Hallow Clips keeps the last moments in a rolling buffer (it keeps overwriting itself).
- Press Ctrl+Alt+S and the last 30 seconds are saved as an MP4 in Videos\Hallow Clips\<game>.
- Your game sound and your mic are on separate tracks (turn the mic off in the app). "Own sound tracks" also puts
  the game itself and Discord (or Spotify, your browser, TeamSpeak) on tracks of their own (Windows 10 2004 or newer).
  "Sound in exports" picks what goes into Discord copies, phone versions and highlight reels.
- Recording runs on your graphics card (NVIDIA, AMD or Intel), so games barely notice.
- 15 s to 2 min clips, 30 or 60 fps, three quality levels, pick your screen.
- Highlight reel (above your clips): tick clips, order them, join them into one video in the Highlights folder.
- The ... button on a clip: set a title, pick its thumbnail, export a GIF or a 9:16 phone version.
- Ctrl+Alt+P saves a screenshot (Videos\Hallow Clips\<game>\Screenshots). Ctrl+Alt+B marks a moment while recording -
  markers become chapters in the clip and jump points when you trim.
- Ctrl+Alt+R (or Record session) records everything until you press it again - saved as an MP4 in the same folder.
- Video format: H.264 plays everywhere; HEVC and AV1 make smaller files (only offered when your graphics card can).
  Windows' own player needs the HEVC / AV1 Video Extension (Microsoft Store) for those.

GOOD TO KNOW
- Updates: Hallow Clips checks GitHub once a day and shows an Update button; it always asks first and never
  touches FFmpeg.
- If Hallow Clips ever crashes, a report is saved in %LOCALAPPDATA%\HallowClips\crashes (only on your PC, never sent)
  and the app tells you next time. Recording always stops with the app.
- The accent colour follows the one you pick in Hallow Toolkit (turn off "Use the Hallow accent" to keep orange).
- With Hallow Hub running, Clips has no tray icon of its own: it's in the Hallow icon's menu (Save clip, Record session,
  Replay buffer, Open clips folder, Exit). Turn off "Use the Hallow icon" in the app to get its own icon back.
- The buffer only exists on your PC and is overwritten constantly; nothing is uploaded anywhere.
- Deleting a clip in the app sends it to the Recycle Bin. The space limit (off unless you pick one) does the same with
  the oldest clips that aren't favourites when the folder grows past it.
- Trim keeps the original and saves a trimmed copy next to it. Discord copies are kept for 2 days in
  %LOCALAPPDATA%\HallowClips\discord.

CREDITS
Recording by FFmpeg (https://ffmpeg.org), LGPL build from github.com/BtbN/FFmpeg-Builds - downloaded separately, not included.
Icons: Font Awesome Free 7.3.1 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.