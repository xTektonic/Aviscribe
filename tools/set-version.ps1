[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$versionMatch = [regex]::Match(
    $Version,
    '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)$'
)
if (-not $versionMatch.Success) {
    throw "Invalid version '$Version'. Use major.minor.patch with non-negative integers and no leading zeroes (for example, 1.2.3)."
}

$majorText = $versionMatch.Groups['major'].Value
$minorText = $versionMatch.Groups['minor'].Value
$patchText = $versionMatch.Groups['patch'].Value
if ($majorText.Length -gt 3 -or $minorText.Length -gt 3 -or $patchText.Length -gt 5) {
    throw "Invalid version '$Version'. Cross-platform packages require major and minor to be at most 255, and patch to be at most 65534."
}

$major = [int]$majorText
$minor = [int]$minorText
$patch = [int]$patchText
if ($major -gt 255 -or $minor -gt 255 -or $patch -gt 65534) {
    throw "Invalid version '$Version'. Cross-platform packages require major and minor to be at most 255, and patch to be at most 65534."
}

$assemblyVersion = "$Version.0"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$targets = @(
    [pscustomobject]@{
        Path = "Directory.Build.props"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(\s*<Version>)[^<]+(</Version>\s*)$'; Value = '${1}' + $Version + '${2}' }
            [pscustomobject]@{ Pattern = '(?m)^(\s*<AssemblyVersion>)[^<]+(</AssemblyVersion>\s*)$'; Value = '${1}' + $assemblyVersion + '${2}' }
            [pscustomobject]@{ Pattern = '(?m)^(\s*<FileVersion>)[^<]+(</FileVersion>\s*)$'; Value = '${1}' + $assemblyVersion + '${2}' }
        )
    }
    [pscustomobject]@{
        Path = ".github/workflows/desktop-ci.yml"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(\s*AVISCRIBE_VERSION:\s*")[^"]+("\s*)$'; Value = '${1}' + $Version + '${2}' }
        )
    }
    [pscustomobject]@{
        Path = "packaging/windows/package.ps1"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(\s*\[string\]\$Version\s*=\s*")[^"]+(",\s*)$'; Value = '${1}' + $Version + '${2}' }
        )
    }
    [pscustomobject]@{
        Path = "packaging/windows/Aviscribe.Package.wixproj"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(\s*<AppVersion Condition="[^"]+">)[^<]+(</AppVersion>\s*)$'; Value = '${1}' + $Version + '${2}' }
        )
    }
    [pscustomobject]@{
        Path = "packaging/macos/package.sh"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(version="\$\{1:-)[^}]+(\}"\s*)$'; Value = '${1}' + $Version + '${2}' }
        )
    }
    [pscustomobject]@{
        Path = "packaging/linux/package.sh"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(version="\$\{1:-)[^}]+(\}"\s*)$'; Value = '${1}' + $Version + '${2}' }
        )
    }
    [pscustomobject]@{
        Path = "src/Aviscribe.Windows.Capture/Properties/AssemblyInfo.cs"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(\[assembly: AssemblyVersion\(")[^"]+("\)\]\s*)$'; Value = '${1}' + $assemblyVersion + '${2}' }
            [pscustomobject]@{ Pattern = '(?m)^(\[assembly: AssemblyFileVersion\(")[^"]+("\)\]\s*)$'; Value = '${1}' + $assemblyVersion + '${2}' }
        )
    }
    [pscustomobject]@{
        Path = "README.md"
        Replacements = @(
            [pscustomobject]@{ Pattern = '(?m)^(\./tools/set-version\.ps1 )\S+(\s*)$'; Value = '${1}' + $Version + '${2}' }
            [pscustomobject]@{ Pattern = '(?m)^(\./packaging/windows/package\.ps1 -Version )\S+(\s*)$'; Value = '${1}' + $Version + '${2}' }
            [pscustomobject]@{ Pattern = '(?m)^(bash packaging/macos/package\.sh )\S+(\s*)$'; Value = '${1}' + $Version + '${2}' }
            [pscustomobject]@{ Pattern = '(?m)^(bash packaging/linux/package\.sh )\S+(\s*)$'; Value = '${1}' + $Version + '${2}' }
        )
    }
)

# Prepare every edit before writing anything, so a stale or malformed target file
# cannot leave the repository with only some version fields updated.
$pendingWrites = foreach ($target in $targets) {
    $fullPath = Join-Path $repoRoot $target.Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Version target not found: $($target.Path)"
    }

    $bytes = [System.IO.File]::ReadAllBytes($fullPath)
    $hasUtf8Bom = $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = [System.Text.UTF8Encoding]::new($hasUtf8Bom)
    $content = [System.IO.File]::ReadAllText($fullPath)
    $originalContent = $content
    foreach ($replacement in $target.Replacements) {
        $matches = [regex]::Matches($content, $replacement.Pattern)
        if ($matches.Count -ne 1) {
            throw "Expected one version field matching '$($replacement.Pattern)' in $($target.Path), but found $($matches.Count). No files were changed."
        }

        $content = [regex]::Replace($content, $replacement.Pattern, $replacement.Value)
    }

    [pscustomobject]@{
        Path = $fullPath
        RelativePath = $target.Path
        Content = $content
        Encoding = $encoding
        Changed = $content -cne $originalContent
    }
}

$filesWritten = 0
foreach ($pendingWrite in $pendingWrites) {
    if (-not $pendingWrite.Changed) {
        continue
    }

    if ($PSCmdlet.ShouldProcess($pendingWrite.RelativePath, "Set Aviscribe version to $Version")) {
        [System.IO.File]::WriteAllText($pendingWrite.Path, $pendingWrite.Content, $pendingWrite.Encoding)
        $filesWritten++
    }
}

if ($filesWritten -gt 0) {
    Write-Host "Aviscribe version updated to $Version in $filesWritten files."
} else {
    Write-Host "Aviscribe version $Version validated; no files were changed."
}
