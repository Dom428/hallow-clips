HALLOW CLIPS - instant replay for your games

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)
4. The first time, click "Set up FFmpeg" - it downloads the free FFmpeg recorder (about 171 MB) from GitHub
   and checks it before use.

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
- Your game sound and your mic are on separate tracks (turn the mic off in the app).
- Recording runs on your graphics card (NVIDIA, AMD or Intel), so games barely notice.
- 15 s to 2 min clips, 30 or 60 fps, three quality levels, pick your screen.

GOOD TO KNOW
- The buffer only exists on your PC and is overwritten constantly; nothing is uploaded anywhere.
- Deleting a clip in the app sends it to the Recycle Bin.
- Trim keeps the original and saves a trimmed copy next to it. Discord copies are kept for 2 days in
  %LOCALAPPDATA%\HallowClips\discord.

CREDITS
Recording by FFmpeg (https://ffmpeg.org), LGPL build from github.com/BtbN/FFmpeg-Builds - downloaded separately, not included.
Icons: Font Awesome Free 7.3.1 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.