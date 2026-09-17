param([Parameter(Mandatory)][string]$MsiPath)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "desktop-shortcut.ps1") -MsiPath $MsiPath -VerifyOnly

# Only run costing/selection in a safe MSI session. Never install the product,
# run custom actions, or change this machine's installed applications.
function Invoke-MsiMethod($Object, [string]$Name, [object[]]$Arguments = @()) {
    $Object.GetType().InvokeMember($Name, "InvokeMethod", $null, $Object, $Arguments)
}
function Get-MsiProperty($Object, [string]$Name, [string]$Key) {
    $Object.GetType().InvokeMember($Name, "GetProperty", $null, $Object, @($Key))
}
function Set-MsiProperty($Object, [string]$Name, [string]$Key, $Value) {
    [void]$Object.GetType().InvokeMember($Name, "SetProperty", $null, $Object, @($Key, $Value))
}
function Assert-Selection($Session, [int]$Expected) {
    $actual = Get-MsiProperty $Session "FeatureRequestState" "AviscribeDesktopShortcut"
    if ($actual -ne $Expected) { throw "Desktop selection: expected $Expected, got $actual." }
    $main = Get-MsiProperty $Session "FeatureRequestState" "WixDefaultFeature"
    if ($main -ne 3) { throw "Required application/Start menu feature was deselected: $main." }
}

$installer = New-Object -ComObject WindowsInstaller.Installer
try {
    $path = [string](Resolve-Path -LiteralPath $MsiPath).Path
    foreach ($choice in @('', '0', '1')) {
        $session = Invoke-MsiMethod $installer "OpenPackage" @($path, 1)
        try {
            Set-MsiProperty $session "Property" "AVISCRIBE_DESKTOP_SHORTCUT" $choice
            foreach ($action in @('CostInitialize', 'FileCost', 'CostFinalize')) {
                $result = Invoke-MsiMethod $session "DoAction" @($action)
                if ($result -ne 1) { throw "$action failed: $result." }
            }
            Invoke-MsiMethod $session "SetInstallLevel" @(1)
            $expected = if ($choice -eq '1') { 3 } else { -1 }
            Assert-Selection $session $expected

            # Exercise the same selection states as AddLocal/Remove events,
            # including selecting then unselecting after navigating Back.
            Set-MsiProperty $session "FeatureRequestState" "AviscribeDesktopShortcut" 3
            Assert-Selection $session 3
            Set-MsiProperty $session "FeatureRequestState" "AviscribeDesktopShortcut" 2
            Assert-Selection $session 2
            Set-MsiProperty $session "FeatureRequestState" "AviscribeDesktopShortcut" 3
            Assert-Selection $session 3
        }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) }
    }
    Write-Host "Desktop shortcut selection tests passed (default, explicit off/on, and toggling). No application was installed."
}
finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
