param(
    [Parameter(Mandatory)]
    [string]$MsiPath,
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

# Velopack 1.2.0 exposes a fixed shortcut list, but no optional-shortcut UI.
# Customize its unsigned MSI transactionally, retaining its shortcut component
# (and therefore native repair/uninstall ownership) and all updater payloads.
# Fail closed if a future Velopack template changes the expected MSI structure.
function Invoke-MsiMethod($Object, [string]$Name, [object[]]$Arguments = @()) {
    $Object.GetType().InvokeMember($Name, "InvokeMethod", $null, $Object, $Arguments)
}

function Get-MsiRows([string]$Sql) {
    $view = Invoke-MsiMethod $database "OpenView" @($Sql)
    try {
        [void](Invoke-MsiMethod $view "Execute")
        while ($record = Invoke-MsiMethod $view "Fetch") {
            try {
                $count = $record.GetType().InvokeMember("FieldCount", "GetProperty", $null, $record, $null)
                [string[]]$values = for ($index = 1; $index -le $count; $index++) {
                    $record.GetType().InvokeMember("StringData", "GetProperty", $null, $record, @($index))
                }
                ,$values
            }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    }
    finally {
        [void](Invoke-MsiMethod $view "Close")
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Invoke-MsiSql([string]$Sql) {
    $view = Invoke-MsiMethod $database "OpenView" @($Sql)
    try { [void](Invoke-MsiMethod $view "Execute") }
    catch { throw "MSI statement failed: $Sql : $_" }
    finally {
        [void](Invoke-MsiMethod $view "Close")
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Assert-MsiValue([string]$Sql, [string]$Expected) {
    $rows = @(Get-MsiRows $Sql)
    if ($rows.Count -ne 1 -or $rows[0][0] -cne $Expected) {
        throw "Unexpected MSI structure: $Sql (expected '$Expected'; rows: $($rows | ConvertTo-Json -Compress))."
    }
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
try {
    $path = [string](Resolve-Path -LiteralPath $MsiPath).Path
    $mode = if ($VerifyOnly) { 0 } else { 1 } # read-only or transactional
    $database = Invoke-MsiMethod $installer "OpenDatabase" @($path, $mode)
    if (-not $VerifyOnly) {
        Assert-MsiValue "SELECT Directory_ FROM Component WHERE Component = 'ApplicationDesktopShortcut'" "DesktopFolder"
        Assert-MsiValue "SELECT Feature_ FROM FeatureComponents WHERE Component_ = 'ApplicationDesktopShortcut'" "WixDefaultFeature"
        Assert-MsiValue "SELECT Control_Next FROM Control WHERE Dialog_ = 'VerifyReadyDlg' AND Control = 'Back'" "BannerBitmap"

        # Level 200 is above the normal install level (1), so silent/default
        # installs omit the shortcut. Unlike level 0 it remains selectable.
        Invoke-MsiSql "INSERT INTO Feature (Feature, Title, Display, Level, Attributes) VALUES ('AviscribeDesktopShortcut', 'Desktop shortcut', 0, 200, 8)"
        Invoke-MsiSql "DELETE FROM FeatureComponents WHERE Component_ = 'ApplicationDesktopShortcut'"
        Invoke-MsiSql "INSERT INTO FeatureComponents (Feature_, Component_) VALUES ('AviscribeDesktopShortcut', 'ApplicationDesktopShortcut')"

        # No default property value: the checkbox starts unchecked. A public,
        # secure property also supports unattended installs with value 1.
        $secure = @(Get-MsiRows "SELECT Value FROM Property WHERE Property = 'SecureCustomProperties'")
        if ($secure.Count -eq 1) {
            $value = ($secure[0][0] + ';AVISCRIBE_DESKTOP_SHORTCUT').Replace("'", "''")
            Invoke-MsiSql "UPDATE Property SET Value = '$value' WHERE Property = 'SecureCustomProperties'"
        }
        else {
            Invoke-MsiSql "INSERT INTO Property (Property, Value) VALUES ('SecureCustomProperties', 'AVISCRIBE_DESKTOP_SHORTCUT')"
        }
        if (@(Get-MsiRows "SELECT Name FROM _Tables WHERE Name = 'Condition'").Count -eq 0) {
            Invoke-MsiSql 'CREATE TABLE `Condition` (`Feature_` CHAR(38) NOT NULL, `Level` SHORT NOT NULL, `Condition` CHAR(255) PRIMARY KEY `Feature_`, `Level`)'
        }
        Invoke-MsiSql "INSERT INTO Condition (Feature_, Level, Condition) VALUES ('AviscribeDesktopShortcut', 1, 'AVISCRIBE_DESKTOP_SHORTCUT = 1')"
        Invoke-MsiSql "INSERT INTO CheckBox (Property, Value) VALUES ('AVISCRIBE_DESKTOP_SHORTCUT', '1')"
        Invoke-MsiSql "INSERT INTO Control (Dialog_, Control, Type, X, Y, Width, Height, Attributes, Property, Text, Control_Next) VALUES ('VerifyReadyDlg', 'AviscribeDesktopShortcut', 'CheckBox', 25, 160, 320, 18, 3, 'AVISCRIBE_DESKTOP_SHORTCUT', 'Create a &desktop shortcut (all users)', 'BannerBitmap')"
        Invoke-MsiSql "UPDATE Control SET Control_Next = 'AviscribeDesktopShortcut' WHERE Dialog_ = 'VerifyReadyDlg' AND Control = 'Back'"
        Invoke-MsiSql "INSERT INTO ControlCondition (Dialog_, Control_, Action, Condition) VALUES ('VerifyReadyDlg', 'AviscribeDesktopShortcut', 'Hide', 'Installed')"

        # CostFinalize has already run before VerifyReadyDlg. Explicitly select
        # or deselect the feature before ending the dialog, including when the
        # user changes their mind after going Back. Quiet installs use Condition.
        foreach ($button in @('Install', 'InstallNoShield')) {
            $orders = @(Get-MsiRows "SELECT Ordering FROM ControlEvent WHERE Dialog_ = 'VerifyReadyDlg' AND Control_ = '$button'") |
                ForEach-Object { [int]$_[0] } | Sort-Object -Descending -Unique
            foreach ($order in $orders) {
                $newOrder = $order + 10
                Invoke-MsiSql "UPDATE ControlEvent SET Ordering = $newOrder WHERE Dialog_ = 'VerifyReadyDlg' AND Control_ = '$button' AND Ordering = $order"
            }
            Invoke-MsiSql "INSERT INTO ControlEvent (Dialog_, Control_, Event, Argument, Condition, Ordering) VALUES ('VerifyReadyDlg', '$button', 'AddLocal', 'AviscribeDesktopShortcut', 'NOT Installed AND AVISCRIBE_DESKTOP_SHORTCUT = 1', 1)"
            Invoke-MsiSql "INSERT INTO ControlEvent (Dialog_, Control_, Event, Argument, Condition, Ordering) VALUES ('VerifyReadyDlg', '$button', 'Remove', 'AviscribeDesktopShortcut', 'NOT Installed AND NOT (AVISCRIBE_DESKTOP_SHORTCUT = 1)', 2)"
        }
    }

    Assert-MsiValue "SELECT Feature_ FROM FeatureComponents WHERE Component_ = 'ApplicationDesktopShortcut'" "AviscribeDesktopShortcut"
    Assert-MsiValue "SELECT Level FROM Feature WHERE Feature = 'AviscribeDesktopShortcut'" "200"
    Assert-MsiValue "SELECT Condition FROM Condition WHERE Feature_ = 'AviscribeDesktopShortcut'" "AVISCRIBE_DESKTOP_SHORTCUT = 1"
    Assert-MsiValue "SELECT Property FROM Control WHERE Dialog_ = 'VerifyReadyDlg' AND Control = 'AviscribeDesktopShortcut'" "AVISCRIBE_DESKTOP_SHORTCUT"
    Assert-MsiValue "SELECT Value FROM CheckBox WHERE Property = 'AVISCRIBE_DESKTOP_SHORTCUT'" "1"
    Assert-MsiValue "SELECT Feature_ FROM FeatureComponents WHERE Component_ = 'ApplicationStartMenuRootShortcut'" "WixDefaultFeature"
    Assert-MsiValue "SELECT Target FROM Shortcut WHERE Shortcut = 'ApplicationDesktopShortcut'" "[INSTALLFOLDER]Aviscribe.exe"
    Assert-MsiValue "SELECT Value FROM Property WHERE Property = 'WIXUI_EXITDIALOGOPTIONALCHECKBOX'" "1"
    if (@(Get-MsiRows "SELECT Value FROM Property WHERE Property = 'AVISCRIBE_DESKTOP_SHORTCUT'").Count -ne 0) {
        throw "The desktop shortcut must be unchecked by default."
    }
    foreach ($button in @('Install', 'InstallNoShield')) {
        Assert-MsiValue "SELECT Ordering FROM ControlEvent WHERE Dialog_ = 'VerifyReadyDlg' AND Control_ = '$button' AND Event = 'AddLocal'" "1"
        Assert-MsiValue "SELECT Ordering FROM ControlEvent WHERE Dialog_ = 'VerifyReadyDlg' AND Control_ = '$button' AND Event = 'Remove'" "2"
        Assert-MsiValue "SELECT Condition FROM ControlEvent WHERE Dialog_ = 'VerifyReadyDlg' AND Control_ = '$button' AND Event = 'AddLocal'" "NOT Installed AND AVISCRIBE_DESKTOP_SHORTCUT = 1"
        Assert-MsiValue "SELECT Condition FROM ControlEvent WHERE Dialog_ = 'VerifyReadyDlg' AND Control_ = '$button' AND Event = 'Remove'" "NOT Installed AND NOT (AVISCRIBE_DESKTOP_SHORTCUT = 1)"
        foreach ($row in @(Get-MsiRows "SELECT Ordering FROM ControlEvent WHERE Dialog_ = 'VerifyReadyDlg' AND Control_ = '$button' AND Event = 'EndDialog'")) {
            if ([int]$row[0] -le 2) { throw "Shortcut selection must precede EndDialog." }
        }
    }
    if (-not $VerifyOnly) { Invoke-MsiMethod $database "Commit" }
    Write-Host "Verified optional desktop shortcut, automatic Start menu shortcut, and default launch selection."
}
finally {
    if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
