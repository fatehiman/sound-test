# Create sweep-400-800.mp3: one smooth sine sweep 400 -> 800 -> 400 Hz, 4 s.
#   f(t)   = 600 - 200 cos(2 pi t / T)            (T = 4 s)
#   phase  = 2 pi 600 t - 200 T sin(2 pi t / T)
# At t = T the phase is 2 pi * 2400 (a whole number of cycles) and f is back
# at 400 Hz, so the end joins the start with no jump when it repeats.
# Needs ffmpeg in PATH.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
ffmpeg -hide_banner -loglevel error -y -f lavfi `
  -i "aevalsrc=0.3*sin(2*PI*600*t-800*sin(PI*t/2)):s=44100:d=4" `
  -ac 2 -c:a libmp3lame -b:a 192k sweep-400-800.mp3
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item sweep-400-800.mp3 | Select-Object FullName, Length
