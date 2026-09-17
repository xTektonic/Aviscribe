param(
    [string]$Version = "1.1.0",
    [string]$Configuration = "Release",
    [string]$ArtifactsDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repoRoot "artifacts"
}

$publishDirectory = Join-Path $ArtifactsDirectory "publish\win-x64"
$packageDirectory = Join-Path $ArtifactsDirectory "packages"
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDirectory, $packageDirectory | Out-Null
Get-ChildItem -LiteralPath $packageDirectory -File |
    Where-Object {
        $_.Name -like "*$Version*" -or
        $_.Extension -eq ".msi" -or
        $_.Name -in @(
            "io.github.xtektonic.aviscribe-win-Setup.exe",
            "releases.win.json",
            "assets.win.json",
            "RELEASES")
    } |
    Remove-Item -Force

dotnet publish (Join-Path $repoRoot "src\Aviscribe.Desktop\Aviscribe.Desktop.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "Velopack tool restore failed."
}

dotnet tool run vpk -- pack `
    --packId io.github.xtektonic.aviscribe `
    --packVersion $Version `
    --packDir $publishDirectory `
    --mainExe Aviscribe.exe `
    --packTitle Aviscribe `
    --packAuthors xTektonic `
    --runtime win-x64 `
    --channel win `
    --icon (Join-Path $repoRoot "src\Aviscribe.Desktop\Assets\aviscribe.ico") `
    --outputDir $packageDirectory `
    --shortcuts Desktop,StartMenuRoot `
    --msi `
    --msiVersion "$Version.0" `
    --instLocation PerMachine `
    --noPortable
if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed."
}

Get-ChildItem -LiteralPath $packageDirectory -Filter "*.nupkg" |
    Where-Object { $_.Name -notlike "*$Version*" } |
    Remove-Item -Force
$generatedMsi = Join-Path $packageDirectory "io.github.xtektonic.aviscribe-win.msi"
$releaseMsi = Join-Path $packageDirectory "Aviscribe-$Version-win-x64.msi"
if (-not (Test-Path -LiteralPath $generatedMsi)) {
    throw "Velopack did not produce the expected MSI."
}
& (Join-Path $PSScriptRoot "desktop-shortcut.ps1") -MsiPath $generatedMsi
Move-Item -LiteralPath $generatedMsi -Destination $releaseMsi -Force
