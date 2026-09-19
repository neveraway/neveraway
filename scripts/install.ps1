# NeverAway installer / upgrader (Windows).
#
#   irm https://raw.githubusercontent.com/neveraway/neveraway/master/scripts/install.ps1 | iex
#
# Resolves the latest GitHub release, downloads the win-x64 zip, and
# verifies the archive signature before changing the installed copy.
# Windows PowerShell 5.1 with .NET Framework 4.8, or PowerShell 7.
$ErrorActionPreference = 'Stop'

function Assert-ReleaseSignature($Archive, $Signature) {
    $parameters = New-Object Security.Cryptography.RSAParameters
    $parameters.Modulus = [Convert]::FromBase64String('qRfYRGm5ck6yV20hbpWZvE8meNgZgPppJQ5XQ5QUlm0d68vkeLhUhl0DtDLcnhNNQR1xHPHgHCDQUwTB3LRRyZoIrj/P9j/WP1jVyaaAhMIKNC1xv8BDdyRJRe+R0Du33DK3z2mJwcgWiN/b711iiYIwmObFA0yISBLj0La0nBf/GgN+SqmYg5PxjQ246ULjriR4gh9Dy1cwOkXfNdIMjn2UE2PO8hQYAbuYLGSc8oC/DK2GQdVUpRlqWi7aAVu8i8cMYHL/zX9LpyzpPFDIXP5cHQG+hzwkrFsfV3qgE6D2qmZktSJ2nQ7yzE4RuJmP0dj9EE+l7nzSK6MdrAEnlnBeAkA3Nyh0m1GP19rZBUuRDnLuHxAkq6igCEDzXjA4RmagXb1dgVbef8HK7A7MqKUHuJux8acMENqYHUoBOl970ZH4HUP4m68fOOuOMn8T4u87DTj4y/SmobVXkhTWC0K3PxcAWj0Vi8HsflmtjACgHF9y9LFekTs/CCq4/Nu7')
    $parameters.Exponent = [Convert]::FromBase64String('AQAB')
    $rsa = [Security.Cryptography.RSA]::Create()
    $stream = $null
    try {
        $rsa.ImportParameters($parameters)
        $bytes = [IO.File]::ReadAllBytes($Signature)
        if ($bytes.Length -ne 384) { throw 'invalid release signature length' }
        $stream = [IO.File]::OpenRead($Archive)
        if (-not $rsa.VerifyData($stream, $bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
            throw 'release signature verification failed'
        }
    } finally {
        if ($stream) { $stream.Dispose() }
        $rsa.Dispose()
    }
}

$repo = 'neveraway/neveraway'
$dir  = Join-Path $env:LOCALAPPDATA 'NeverAway'
$exe  = Join-Path $dir 'neveraway.exe'

$rel   = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$asset = @($rel.assets | Where-Object name -eq 'NeverAway-win-x64.zip')
$signature = @($rel.assets | Where-Object name -eq 'NeverAway-win-x64.zip.sig')
if ($asset.Count -ne 1 -or $signature.Count -ne 1) { throw 'latest release must contain one Windows archive and its signature' }
Write-Host "downloading $($asset.browser_download_url) ($($rel.tag_name))"

$stage = Join-Path $env:LOCALAPPDATA ('.NeverAway-' + [Guid]::NewGuid().ToString('N'))
$old = Join-Path $stage 'previous'
$ready = Join-Path $stage 'ready'
$installed = $false
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $zip = Join-Path $stage 'NeverAway-win-x64.zip'
    $sig = $zip + '.sig'
    Invoke-WebRequest $asset[0].browser_download_url -UseBasicParsing -OutFile $zip
    Invoke-WebRequest $signature[0].browser_download_url -UseBasicParsing -OutFile $sig
    Assert-ReleaseSignature $zip $sig
    Expand-Archive -Path $zip -DestinationPath $ready
    if (-not (Test-Path -LiteralPath (Join-Path $ready 'neveraway.exe') -PathType Leaf)) { throw 'verified archive is missing neveraway.exe' }

    # Keep Windows' downloaded-file protections, including after extraction.
    Get-ChildItem -LiteralPath $ready -File -Recurse | ForEach-Object {
        Set-Content -LiteralPath $_.FullName -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
    }
    Get-Process neveraway -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $dir) { Move-Item -LiteralPath $dir -Destination $old }
    Move-Item -LiteralPath $ready -Destination $dir
    $installed = $true
} finally {
    if (-not $installed -and (Test-Path -LiteralPath $old) -and -not (Test-Path -LiteralPath $dir)) {
        Move-Item -LiteralPath $old -Destination $dir
    }
    if ($installed -or -not (Test-Path -LiteralPath $old)) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    } else {
        Write-Warning "previous installation retained at $old"
    }
}

$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'NeverAway.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dir
$sc.Save()
Start-Process -FilePath $exe
Write-Host 'NeverAway installed: look for the tray icon near the clock.'
Write-Host "To start at login: Win+R, 'shell:startup', copy the NeverAway shortcut there."
