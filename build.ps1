# Build SoundTest.exe into .\dist (single file, needs the .NET 8 Desktop Runtime).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet publish SoundTest.csproj -c Release -o dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item dist\SoundTest.exe | Select-Object FullName, Length, LastWriteTime
