<#
    test-ui.ps1 - the UI gate for Portal, run in the Windows CI guest.

    1. Builds the solution and runs the unit tests.
    2. Renders every scene of the offscreen harness (app/Portal.Shots) in both themes and checks
       each PNG is a real 1280x800 image.
    3. Starts the real portal.exe with an empty, private profile, finds its window through UI
       Automation, types an address into the connect form, checks the Save button reacts,
       captures the window, and closes it through its WindowPattern. No positional input is sent.

    Prints the verify contract - PASS:/FAIL: lines and one final RESULT: line - and exits 0 on
    pass, 1 on fail. Evidence lands in target\shots-wpf and target\ui-check.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$shotsDir = Join-Path $repoRoot 'target\shots-wpf'
$checkDir = Join-Path $repoRoot 'target\ui-check'
$MinPngBytes = 10000
$MinScenesPerTheme = 12

$script:Pass = 0
$script:Fail = 0

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    if ($Condition) { Write-Output "PASS: $Message"; $script:Pass++ }
    else { Write-Output "FAIL: $Message"; $script:Fail++ }
}

function Complete-Suite {
    Write-Output ''
    if ($script:Fail -gt 0) { Write-Output 'RESULT: FAIL'; exit 1 }
    Write-Output 'RESULT: PASS'
    exit 0
}

function Invoke-Step {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][scriptblock]$Command)
    $output = & $Command 2>&1 | Out-String
    $ok = $LASTEXITCODE -eq 0
    Assert-True $ok $Name
    if (-not $ok) { ($output -split "`r?`n" | Select-Object -Last 30) | ForEach-Object { Write-Output "  $_" } }
    return $ok
}

# --- toolchain ---------------------------------------------------------------
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$sdks = if ($dotnet) { (& dotnet --list-sdks 2>$null | Out-String) } else { '' }
if (-not ($sdks -match '(?m)^10\.')) {
    Write-Output 'FAIL: the .NET 10 SDK is not installed in this environment'
    Write-Output 'RESULT: FAIL'
    exit 1
}
Write-Output "PASS: .NET 10 SDK is available"
$script:Pass++

# --- build and unit tests ----------------------------------------------------
Set-Location -LiteralPath $repoRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if (-not (Invoke-Step 'dotnet build Portal.slnx -c Release' { dotnet build Portal.slnx -c Release -nologo -v q })) { Complete-Suite }
Invoke-Step 'dotnet test (unit tests; live and Docker tests skip themselves)' { dotnet test Portal.slnx -c Release --no-build -nologo -v q } | Out-Null

# --- offscreen screens -------------------------------------------------------
if (Test-Path -LiteralPath $shotsDir) { Remove-Item -LiteralPath $shotsDir -Recurse -Force }
$harness = Join-Path $repoRoot 'app\Portal.Shots\bin\Release\net10.0-windows\Portal.Shots.exe'
Assert-True (Test-Path -LiteralPath $harness) 'the screenshot harness was built'
if (-not (Test-Path -LiteralPath $harness)) { Complete-Suite }

function Get-PngSize {
    param([Parameter(Mandatory)][string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 24) { return $null }
    $signature = @(137, 80, 78, 71, 13, 10, 26, 10)
    for ($i = 0; $i -lt 8; $i++) { if ($bytes[$i] -ne $signature[$i]) { return $null } }
    $width = [int]$bytes[16] * 16777216 + [int]$bytes[17] * 65536 + [int]$bytes[18] * 256 + [int]$bytes[19]
    $height = [int]$bytes[20] * 16777216 + [int]$bytes[21] * 65536 + [int]$bytes[22] * 256 + [int]$bytes[23]
    return [PSCustomObject]@{ Width = $width; Height = $height; Bytes = $bytes.Length }
}

foreach ($theme in @(@{ Name = 'dark'; Arg = @(); Index = 'index.json' }, @{ Name = 'light'; Arg = @('--light'); Index = 'index-light.json' })) {
    $out = & $harness $shotsDir @($theme.Arg) 2>&1 | Out-String
    $ok = $LASTEXITCODE -eq 0
    Assert-True $ok "the harness rendered the $($theme.Name) theme"
    if (-not $ok) { ($out -split "`r?`n" | Select-Object -Last 20) | ForEach-Object { Write-Output "  $_" }; continue }
    $indexPath = Join-Path $shotsDir $theme.Index
    $scenes = @(Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json)
    Assert-True ($scenes.Count -ge $MinScenesPerTheme) "the $($theme.Name) manifest lists at least $MinScenesPerTheme scenes (has $($scenes.Count))"
    $broken = @()
    foreach ($scene in $scenes) {
        $path = Join-Path $shotsDir "$($scene.name).png"
        if (-not (Test-Path -LiteralPath $path)) { $broken += "$($scene.name): missing"; continue }
        $png = Get-PngSize -Path $path
        if (-not $png) { $broken += "$($scene.name): not a PNG"; continue }
        if ($png.Width -ne 1280 -or $png.Height -ne 800) { $broken += "$($scene.name): $($png.Width)x$($png.Height)"; continue }
        if ($png.Bytes -lt $MinPngBytes) { $broken += "$($scene.name): only $($png.Bytes) bytes" }
    }
    Assert-True ($broken.Count -eq 0) "every $($theme.Name) scene is a non-blank 1280x800 PNG"
    foreach ($problem in $broken) { Write-Output "  - $problem" }
}

# --- the real window ---------------------------------------------------------
if (Test-Path -LiteralPath $checkDir) { Remove-Item -LiteralPath $checkDir -Recurse -Force }
New-Item -ItemType Directory -Path $checkDir | Out-Null
$profileDir = Join-Path $env:TEMP "portal-ui-check-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $profileDir 'config'), (Join-Path $profileDir 'ssh') -Force | Out-Null
$exe = Join-Path $repoRoot 'app\Portal\bin\Release\net10.0-windows\portal.exe'
Assert-True (Test-Path -LiteralPath $exe) 'portal.exe was built'
if (-not (Test-Path -LiteralPath $exe)) { Complete-Suite }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$env:PORTAL_CONFIG_DIR = Join-Path $profileDir 'config'
$env:PORTAL_SSH_DIR = Join-Path $profileDir 'ssh'
$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Remove-Item Env:\PORTAL_CONFIG_DIR, Env:\PORTAL_SSH_DIR -ErrorAction SilentlyContinue

$root = [System.Windows.Automation.AutomationElement]::RootElement
$byPid = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$window = $null
$deadline = (Get-Date).AddSeconds(45)
while (-not $window -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byPid)
}
Assert-True ($null -ne $window) 'portal.exe opened a window'
if (-not $window) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; Complete-Suite }
Assert-True ($window.Current.Name -eq 'Portal') "the window title is 'Portal' (was '$($window.Current.Name)')"

function Find-ByName([string]$name, $controlType = $null) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    if ($controlType) {
        $typeCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
        $cond = New-Object System.Windows.Automation.AndCondition($cond, $typeCond)
    }
    $end = (Get-Date).AddSeconds(10)
    do {
        $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $end)
    return $null
}

Assert-True ($null -ne (Find-ByName 'Your servers')) 'the connect screen shows "Your servers"'
Assert-True ($null -ne (Find-ByName 'Add a server')) 'the add-server form is shown'
$address = Find-ByName 'Address' ([System.Windows.Automation.ControlType]::Edit)
$save = Find-ByName 'Save and connect' ([System.Windows.Automation.ControlType]::Button)
Assert-True ($null -ne $address -and $null -ne $save) 'the Address field and the Save button are exposed to UI Automation'
if ($address -and $save) {
    Assert-True (-not $save.Current.IsEnabled) 'Save is disabled while the address is empty'
    $address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('ui-check.invalid')
    Start-Sleep -Milliseconds 600
    Assert-True ($save.Current.IsEnabled) 'Save turns on once an address is typed'
}

$rect = $window.Current.BoundingRectangle
if ($rect.Width -gt 0 -and $rect.Height -gt 0) {
    $bitmap = New-Object System.Drawing.Bitmap ([int]$rect.Width), ([int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size)
    $bitmap.Save((Join-Path $checkDir 'connect-window.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
Assert-True (Test-Path -LiteralPath (Join-Path $checkDir 'connect-window.png')) 'captured the live window'

$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
$exited = $proc.WaitForExit(15000)
Assert-True $exited 'the window closes through its WindowPattern and the process exits'
if (-not $exited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
$wrote = Test-Path -LiteralPath (Join-Path $profileDir 'config\window.json')
Assert-True $wrote 'closing saved the window placement into the private profile'
Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue

Complete-Suite
