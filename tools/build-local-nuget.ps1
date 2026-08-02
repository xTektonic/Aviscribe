param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$FlashCapVersion = "1.11.9",
    [string]$FlashCapCommit = "ba2de264b0c6bcd77c3ad2b65a83963393631e88",
    [string]$PipeWireNetVersion = "0.2.1-alpha-aviscribe.1",
    [string]$PipeWireNetCommit = "263081ab3d5117c487cf8174548d98c38f4d32e8"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $output -Force | Out-Null

$temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetTempPath()
}
else {
    $env:RUNNER_TEMP
}
$sourceRoot = Join-Path $temporaryRoot (
    "AviscribeDependencies-" + [guid]::NewGuid().ToString("N"))
$flashCapSource = Join-Path $sourceRoot "FlashCap"
$pipeWireNetSource = Join-Path $sourceRoot "PipeWire.NET"
New-Item -ItemType Directory -Path $sourceRoot | Out-Null

try {
    Invoke-Checked "FlashCap clone" {
        git clone https://github.com/xTektonic/FlashCap.git $flashCapSource
    }
    Invoke-Checked "FlashCap checkout" {
        git -C $flashCapSource checkout --detach $FlashCapCommit
    }

    foreach ($project in @("FlashCap.Core", "FlashCap")) {
        $projectPath = Join-Path $flashCapSource "$project/$project.csproj"
        Invoke-Checked "$project pack" {
            dotnet pack $projectPath `
                --configuration Release `
                --output $output `
                -p:Version=$FlashCapVersion `
                -p:PackageVersion=$FlashCapVersion
        }
    }

    Invoke-Checked "PipeWire.NET clone" {
        git clone https://github.com/xTektonic/PipeWire.NET.git $pipeWireNetSource
    }
    Invoke-Checked "PipeWire.NET checkout" {
        git -C $pipeWireNetSource checkout --detach $PipeWireNetCommit
    }

    $pipeWireNetProject = Join-Path `
        $pipeWireNetSource `
        "src/PipeWire.NET/PipeWire.NET.csproj"
    Invoke-Checked "PipeWire.NET pack" {
        dotnet pack $pipeWireNetProject `
            --configuration Release `
            --output $output `
            -p:TargetFrameworks=net10.0 `
            -p:MinVerVersionOverride=$PipeWireNetVersion `
            -p:Version=$PipeWireNetVersion `
            -p:PackageVersion=$PipeWireNetVersion
    }

    $expectedPackages = @(
        "FlashCap.$FlashCapVersion.nupkg",
        "FlashCap.Core.$FlashCapVersion.nupkg",
        "PipeWire.NET.$PipeWireNetVersion.nupkg"
    )
    foreach ($package in $expectedPackages) {
        $packagePath = Join-Path $output $package
        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            throw "$package was not created."
        }
        Write-Output "Created $packagePath"
    }
}
finally {
    if (Test-Path -LiteralPath $sourceRoot) {
        Remove-Item -LiteralPath $sourceRoot -Recurse -Force
    }
}
