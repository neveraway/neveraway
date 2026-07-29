# NeverAway installer / upgrader (Windows).
#
#   irm https://raw.githubusercontent.com/neveraway/neveraway/master/scripts/install.ps1 | iex
#
# Resolves the latest GitHub release, downloads the win-x64 zip, and
# installs to %LOCALAPPDATA%\NeverAway (no admin needed). If NeverAway
# is running it is stopped first so the exe can be replaced. Strips
# mark-of-the-web from the extracted files so SmartScreen doesn't warn
# on first run, creates a Start Menu shortcut, and launches the app.
$ErrorActionPreference = 'Stop'

$repo = 'neveraway/neveraway'
$dir  = Join-Path $env:LOCALAPPDATA 'NeverAway'
$exe  = Join-Path $dir 'neveraway.exe'

$rel   = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$asset = $rel.assets | Where-Object name -eq 'NeverAway-win-x64.zip'
if (-not $asset) { throw 'could not find NeverAway-win-x64.zip on the latest release' }
Write-Host "downloading $($asset.browser_download_url) ($($rel.tag_name))"

$zip = Join-Path $env:TEMP 'NeverAway-win-x64.zip'
Invoke-WebRequest $asset.browser_download_url -OutFile $zip

# Upgrade path: stop the running instance so the exe isn't locked.
Get-Process neveraway -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $dir | Out-Null
Expand-Archive -Path $zip -DestinationPath $dir -Force
Remove-Item $zip
Get-ChildItem $dir -Recurse | Unblock-File

# Start Menu shortcut.
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'NeverAway.lnk'
$ws  = New-Object -ComObject WScript.Shell
$sc  = $ws.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dir
$sc.Save()

Start-Process $exe
Write-Host 'NeverAway installed: look for the tray icon near the clock.'
Write-Host "To start at login: Win+R, 'shell:startup', copy the NeverAway shortcut there."
