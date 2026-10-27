param([string]$FromFeed = $env:SMOVISION_FEED)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $FromFeed) {
    $release = Join-Path $repo '.local/smovision-release/0.1.0-beta.2'
    New-Item -ItemType Directory -Force -Path $release | Out-Null
    $archive = Join-Path $release 'bundle.zip'
    if (-not (Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -Uri 'https://github.com/xTektonic/SMOVision/releases/download/v0.1.0-beta.2/SMOVision-v0.1.0-beta.2.zip' -OutFile $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne '4bd9324c64608e1534759a40171c98c15229832cd1a4a5cd91318ef37f375b34') {
        throw 'SMOVision beta.2 archive checksum mismatch. Remove the cached bundle.zip and retry, or supply -FromFeed with the released packages directory.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $release 'bundle') -Force
    $FromFeed = Join-Path $release 'bundle/packages'
}
$manifest = Join-Path $PSScriptRoot 'smovision-packages.sha256'
$packages = foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9.-]+\.nupkg)$') { throw "Invalid package manifest line: $line" }
    $expected = $Matches[1]
    $name = $Matches[2]
    $source = Join-Path $FromFeed $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing $name. Set SMOVISION_FEED to the packages directory from the SMOVision v0.1.0-beta.2 release bundle." }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Checksum mismatch: $name" }
    $source
}
$destination = Join-Path $repo '.local/nuget/0.1.0-beta.2'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($package in $packages) { Copy-Item -LiteralPath $package -Destination $destination -Force }
Write-Host 'Verified and prepared all nine SMOVision beta.2 packages.'
