# Sound Test

Small Windows app to check if a headset leaks speaker sound into its own microphone.

It plays an audio file (MP3, WAV, M4A, WMA) on repeat and shows two live level meters,
like a softphone (MicroSIP):

- **Speaker** – the output level of the selected speaker device (all sound on that device).
- **Microphone** – the input level of the selected microphone.

If the microphone meter moves together with the speaker meter while nobody talks,
the speaker sound reaches the mic (bad cable/jack, or sound leaking from the ear cups).

## Use

1. Click the folder button and pick an audio file (the last file is remembered).
2. Pick the speaker and the microphone. `Default` uses the Windows default device.
   The reload button refreshes both lists after you plug a device in or out.
3. Click play. Stay silent and watch the microphone meter.

## Build

Needs the .NET 8 SDK.

```powershell
.\build.ps1
```

Output: `dist\SoundTest.exe` (single file, needs the .NET 8 Desktop Runtime).

## Notes

- Audio: [NAudio](https://github.com/naudio/NAudio) (WASAPI shared mode).
- Only one copy can run at a time.
- Settings file: `%APPDATA%\SoundTest\last-file.txt`.
