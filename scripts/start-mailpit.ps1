$ErrorActionPreference = 'Stop'

if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'This launcher requires PowerShell 7 on Windows x64.'
}

$version = 'v1.33.0'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mailpitDirectory = Join-Path $root '.local/mailpit'
$binaryDirectory = Join-Path $mailpitDirectory $version
$executable = Join-Path $binaryDirectory 'mailpit.exe'

if (-not (Test-Path -LiteralPath $executable)) {
    New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
    $archive = Join-Path $binaryDirectory 'mailpit-windows-amd64.zip'
    Invoke-WebRequest "https://github.com/axllent/mailpit/releases/download/$version/mailpit-windows-amd64.zip" -OutFile $archive
    # Pin the official release archive so a changed download cannot execute silently.
    $expectedHash = '47122dd828ff80dcaef62b2952c478b178afadadf066c990489608e537121bfd'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Mailpit download checksum mismatch. The archive was not extracted or executed.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $binaryDirectory -Force
    Remove-Item -LiteralPath $archive
}

Write-Host 'Mailpit inbox: http://localhost:8025 | SMTP: localhost:1025 | Ctrl+C to stop'
& $executable --listen 127.0.0.1:8025 --smtp 127.0.0.1:1025 --database (Join-Path $mailpitDirectory 'messages.db') --disable-version-check
if ($LASTEXITCODE -ne 0) { throw "Mailpit exited with code $LASTEXITCODE. Check whether ports 8025 or 1025 are already in use." }
