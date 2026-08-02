param(
    [string]$Version = "0.3.1",
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

dotnet publish (Join-Path $repoRoot "src\Aviscribe.Desktop\Aviscribe.Desktop.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

dotnet build (Join-Path $PSScriptRoot "Aviscribe.Package.wixproj") `
    --configuration $Configuration `
    --output $packageDirectory `
    -p:PublishDir=$publishDirectory `
    -p:AppVersion=$Version
if ($LASTEXITCODE -ne 0) {
    throw "WiX packaging failed."
}
