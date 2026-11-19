# Hallow Clips

Instant replay for your games - part of the Hallow family with [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Mixer](https://github.com/Dom428/hallow-mixer), [Hallow Vault](https://github.com/Dom428/hallow-vault), [Hallow Overlay](https://github.com/Dom428/hallow-overlay), [Hallow Remote](https://github.com/Dom428/hallow-remote) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit).

- Keeps the last moments of your game in a rolling buffer while a game runs; **Ctrl+Alt+S** saves them as an MP4
- Screen grabbed with Desktop Duplication and encoded on the graphics card (NVENC / AMF / Quick Sync)
- Game sound (WASAPI loopback) and your mic on **separate audio tracks**
- 15 s to 2 min, 30 / 60 fps, quality levels, any screen; clip list with thumbnails

Uses [FFmpeg](https://ffmpeg.org) (LGPL build from BtbN/FFmpeg-Builds), downloaded on first use and checked against GitHub's SHA-256 - not included here.

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## Credits

FFmpeg by the FFmpeg developers (LGPL). Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).