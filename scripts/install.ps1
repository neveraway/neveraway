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
    $parameters.Modulus = [Convert]::FromBase64String('qYlCUK8cudPN3L5jSZ3ohrvRTMWX0FXTxPs4WtGQk389WBYpBrydNmxkA8Ptfkydv3cFbGcHPVeqYmvbxgDFjtHl70aY2FgPuXxn240ARJ5LeFeib2W8TD407eLYJgjh0vlzz7atd1ldLdzLlYL33FnlM9rpbjaqQ6qekU3e1nCRuu8iQNSrYf4C8JG3+tQP46W7YU09P5ouxsgvpleIqIdc1DkWCE6gdukBRUh87FB1ExQB0scGF70lF+vRF+zZKEKbyMMSFTd5+MmG01jdkf8Z2Sy5PP5PklyX1dx/huVYdrKQwxHSpWaNpx7QJxH2onQEtnEpqY+7b4g3dpUjKGSnL/bbo7GmrKak1n5ACbq5ug6KiLcB24rYZ5LTnTYe2nzqtD2FbypELvaDICf8Xv/ANaqQ/R2hY+zviNfPnZ05LZXxmRnaTuMN8M0zFIM5xSriyvRtjBeGFhQrF/72f3XfmoCiNIViT83BW1EjIt37afmmfNBQO8xpDJ3emWhJ')
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
