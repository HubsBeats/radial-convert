Convert files without opening anything: select files in Explorer or on the desktop, hold **Shift**,
right-click, flick toward a format and let go.

| Selected | Up | Right | Down | Left |
|---|---|---|---|---|
| Image | PNG | JPG | PDF | GIF |
| Audio | MP3 | WAV | FLAC | M4A |
| Video | MP4 | WebM | GIF | MP3 |

**Quick start:** download `RadialConvert.exe` and run it. Windows may say "Windows protected your PC"
because the exe isn't code-signed: click More info, then Run anyway. On first run it downloads FFmpeg
once (about 100 MB) for audio and video. Right-click the tray icon to turn on **Start with Windows**
or edit the formats.

**Full install:** unzip `RadialConvert-installer.zip` and run
`powershell -ExecutionPolicy Bypass -File install.ps1`. It builds the exe on your PC (no SmartScreen
warning), installs it to `%LOCALAPPDATA%\RadialConvert`, sets up FFmpeg and starts with Windows.

Needs Windows 10 or 11 (.NET Framework 4.8, already built in).
