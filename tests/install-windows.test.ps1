$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$temp = Join-Path ([IO.Path]::GetTempPath()) ('neveraway-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$payload = Join-Path $temp 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
Set-Content -LiteralPath (Join-Path $payload 'neveraway.exe') -Value 'test binary, never executed'
$fixture = Join-Path $temp 'fixture.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $fixture
& node (Join-Path $PSScriptRoot 'windows-sign-fixture.mjs') $fixture
if ($LASTEXITCODE -ne 0) { throw 'fixture signing failed' }
$public = Get-Content -LiteralPath ($fixture + '.public.json') -Raw | ConvertFrom-Json
$source = Get-Content -LiteralPath (Join-Path $root 'scripts/install.ps1') -Raw
$source = [regex]::Replace($source, '(?<=Modulus = \[Convert\]::FromBase64String\('')[^'']+', $public.modulus)
$savedLocal = $env:LOCALAPPDATA

function Invoke-RestMethod {
    $assets = @([pscustomobject]@{ name = 'NeverAway-win-x64.zip'; browser_download_url = 'https://example.test/archive' })
    if ($script:scenario -ne 'missing signature') { $assets += [pscustomobject]@{ name = 'NeverAway-win-x64.zip.sig'; browser_download_url = 'https://example.test/signature' } }
    [pscustomobject]@{ tag_name = 'vtest'; assets = $assets }
}
function Invoke-WebRequest($Uri, $OutFile, [switch]$UseBasicParsing) {
    if ($Uri -like '*/archive') {
        Copy-Item -LiteralPath $fixture -Destination $OutFile
        if ($script:scenario -eq 'modified zip') { [IO.File]::AppendAllText($OutFile, 'changed') }
    } else {
        $path = if ($script:scenario -eq 'wrong key') { $fixture + '.wrong.sig' } else { $fixture + '.sig' }
        $bytes = [IO.File]::ReadAllBytes($path)
        if ($script:scenario -eq 'modified signature') { $bytes[0] = $bytes[0] -bxor 1 }
        if ($script:scenario -eq 'truncated signature') { $bytes = $bytes[0..382] }
        [IO.File]::WriteAllBytes($OutFile, $bytes)
    }
}
function Get-Process { [pscustomobject]@{ Name = 'neveraway' } }
function Stop-Process { $script:stopped++ }
function Start-Sleep { }
function Start-Process($FilePath) {
    $script:started++
    if (-not (Test-Path -LiteralPath $FilePath)) { throw 'launch path missing' }
}
function Move-Item($LiteralPath, $Destination) {
    if ($script:scenario -eq 'replacement failure' -and $LiteralPath.EndsWith('ready')) { throw 'simulated replacement failure' }
    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination
}
function New-Object($TypeName, $ComObject) {
    if ($ComObject -eq 'WScript.Shell') {
        $shell = [pscustomobject]@{}
        $shell | Add-Member -MemberType ScriptMethod -Name CreateShortcut -Value {
            $shortcut = [pscustomobject]@{ TargetPath = ''; WorkingDirectory = '' }
            $shortcut | Add-Member -MemberType ScriptMethod -Name Save -Value { $script:shortcuts++ }
            return $shortcut
        }
        return $shell
    }
    Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName
}

try {
    foreach ($scenario in @('valid', 'modified zip', 'modified signature', 'truncated signature', 'wrong key', 'missing signature', 'replacement failure')) {
        $script:scenario = $scenario
        $script:stopped = 0
        $script:started = 0
        $script:shortcuts = 0
        $env:LOCALAPPDATA = Join-Path $temp ($scenario -replace ' ', '-')
        $installed = Join-Path $env:LOCALAPPDATA 'NeverAway'
        New-Item -ItemType Directory -Path $installed -Force | Out-Null
        $old = Join-Path $installed 'old.txt'
        Set-Content -LiteralPath $old -Value 'keep the old version'
        $failed = $false
        try { & ([scriptblock]::Create($source)) } catch {
            if ($scenario -eq 'valid') { throw }
            $failed = $true
        }
        if ($scenario -eq 'valid') {
            if ($failed -or $script:started -ne 1 -or $script:shortcuts -ne 1 -or (Test-Path -LiteralPath $old)) { throw 'valid install failed' }
            $zone = Get-Content -LiteralPath (Join-Path $installed 'neveraway.exe') -Stream Zone.Identifier
            if ($zone -notcontains 'ZoneId=3') { throw 'downloaded-file protection missing' }
        } else {
            if (-not $failed -or -not (Test-Path -LiteralPath $old) -or $script:started -or $script:shortcuts) { throw "$scenario changed the installed copy" }
            if ($scenario -ne 'replacement failure' -and $script:stopped) { throw "$scenario stopped the running copy" }
        }
        Write-Host "passed: $scenario"
    }
} finally {
    $env:LOCALAPPDATA = $savedLocal
    Remove-Item -LiteralPath $temp -Recurse -Force
}
