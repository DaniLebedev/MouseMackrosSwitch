#requires -Version 5.1
<#
.SYNOPSIS
  Shows or edits the lighting table of an MBF1000PRO .psf profile.

.DESCRIPTION
  The vendor app keeps lighting as 11 preset rows x 10 LEDs:
    Lighting1ColorSelected : per-LED enable mask (01 = use FixedColor)
    Lighting1FixedColor    : per-LED RRGGBB color
    Lighting1Speed         : 11 values, one per preset
    Lighting1Brightness    : 11 values, one per preset
    Lighting1Direction     : 11 values, one per preset

  Row = preset mode - 1:
    0 Stars Twinkle, 1 Colorful Streaming, 2 Seven Color Stretch,
    3 Seven Color Flashing, 4 Colorful Breath, 5 Seven Color Explosion Flash,
    6 Colorful Coverage, 7 Red Full, 8 Green Full, 9 Blue Full, 10 None.

  The file is UTF-16LE. Edit the ACTIVE profile, normally
  %APPDATA%\GamingMouseData20250321B\config\default2.psf (CurrentProfile=2).

.EXAMPLE
  .\tools\psf-lighting.ps1 -Show

.EXAMPLE
  .\tools\psf-lighting.ps1 -Row 2 -Color 8000FF

.EXAMPLE
  .\tools\psf-lighting.ps1 -Debounce 10

.EXAMPLE
  .\tools\psf-lighting.ps1 -Restore
#>
[CmdletBinding()]
param(
    [string]$Profile = '',
    [int]$Row = 2,
    [string]$Color = '8000FF',
    [int]$Led = -1,
    [ValidateSet(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)]
    [int]$Mode = 3,
    [string]$Name = 'Seven Color Stretch',
    [int]$Speed = -1,
    [int]$Brightness = -1,
    [int]$Direction = -1,
    [int]$Debounce = -1,
    [switch]$Logo,
    [switch]$Show,
    [switch]$Restore,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Resolve the ACTIVE profile from config.ini when -Profile is omitted.
if ([string]::IsNullOrWhiteSpace($Profile)) {
    $configPath = Join-Path $env:APPDATA 'GamingMouseData20250321B\config\config.ini'
    $slot = 0
    if (Test-Path -LiteralPath $configPath) {
        $line = Get-Content $configPath -Encoding Unicode |
            Where-Object { $_ -match '^\s*CurrentProfile\s*=\s*(\d+)' } |
            Select-Object -First 1
        if ($line -match '^\s*CurrentProfile\s*=\s*(\d+)') { $slot = [int]$matches[1] }
    }
    $Profile = Join-Path (Split-Path -Parent $configPath) "default$slot.psf"
    Write-Host "Active profile: CurrentProfile=$slot -> $Profile" -ForegroundColor Yellow
}

function Get-Field([string]$text, [string]$name) {
    $m = [regex]::Match($text, "(?m)^\s*$name=(.*)$")
    if (!$m.Success) { throw "Field $name not found" }
    return $m.Groups[1].Value.Trim()
}

function Set-Field([string]$text, [string]$name, [string]$value) {
    $pattern = "(?m)^(\s*$name=).*$"
    if (![regex]::IsMatch($text, $pattern)) { throw "Field $name not found" }
    return [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $value })
}

function Show-Rows([string]$text, [string]$title) {
    $sel = (Get-Field $text 'Lighting1ColorSelected').TrimEnd(';').Split(';')
    $col = (Get-Field $text 'Lighting1FixedColor').TrimEnd(';').Split(';')
    $speed = (Get-Field $text 'Lighting1Speed').Split('|')
    $bright = (Get-Field $text 'Lighting1Brightness').Split('|')
    $dir = (Get-Field $text 'Lighting1Direction').Split('|')

    Write-Host "=== $title" -ForegroundColor Cyan
    Write-Host ("Mode=" + (Get-Field $text 'Lighting1ModeValue') +
                "  Name=" + (Get-Field $text 'Lighting1Name') +
                "  OffOn=" + (Get-Field $text 'Lighting1OffOn'))
    Write-Host ("KeyDebounce=" + (Get-Field $text 'KeyDebounce') +
                "  Polling=" + (Get-Field $text 'PollingRateName') +
                " (" + (Get-Field $text 'PollingRateValue') + ")")
    for ($r = 0; $r -lt [Math]::Min($sel.Count, 11); $r++) {
        $f = $sel[$r].Split('|')
        $c = $col[$r].Split('|')
        $on = @()
        for ($i = 0; $i -lt [Math]::Min($f.Count, 10); $i++) {
            if ($f[$i] -eq '01') { $on += ("LED$i=#" + $c[$i]) }
        }
        $tail = "speed=$($speed[$r]) bright=$($bright[$r]) dir=$($dir[$r])"
        Write-Host ("row {0}: {1}  [{2}]" -f $r, ($(if ($on.Count) { $on -join ', ' } else { '-' })), $tail)
    }
}

if (!(Test-Path -LiteralPath $Profile)) { throw "Profile not found: $Profile" }

$dir = Split-Path -Parent $Profile
$base = Split-Path -Leaf $Profile

if ($Restore) {
    $backup = Get-ChildItem $dir -Filter "$base.bak-*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$backup) { throw "No backup for $base" }
    Copy-Item $backup.FullName $Profile -Force
    Write-Host "Restored $Profile from $($backup.Name)" -ForegroundColor Green
    return
}

if (!$Show -and (Get-Process GamingMS -ErrorAction SilentlyContinue) -and !$Force) {
    throw "MBF1000PRO is running. Close it first (or pass -Force if you know what you are doing)."
}

$text = [IO.File]::ReadAllText($Profile, [Text.Encoding]::Unicode)

if ($Show) {
    Show-Rows $text $Profile
    return
}

if ($Debounce -ge 0) {
    if ($Debounce -lt 1 -or $Debounce -gt 50) { throw "Debounce must be 1..50 ms" }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupPath = Join-Path $dir "$base.bak-$stamp"
    [IO.File]::WriteAllText($backupPath, $text, [Text.Encoding]::Unicode)
    $text = Set-Field $text 'KeyDebounce' "$Debounce"
    [IO.File]::WriteAllText($Profile, $text, [Text.Encoding]::Unicode)
    Write-Host "Updated KeyDebounce=$Debounce (backup: $backupPath)" -ForegroundColor Green
    Show-Rows $text $Profile
    return
}

if ($Row -lt 0 -or $Row -gt 10) { throw "Row must be 0..10" }
if ($Color -notmatch '^[0-9A-Fa-f]{6}$') { throw "Color must be RRGGBB, e.g. 8000FF" }
if ($Led -ne -1 -and ($Led -lt 0 -or $Led -gt 9)) { throw "Led must be 0..9" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $dir "$base.bak-$stamp"
[IO.File]::WriteAllText($backupPath, $text, [Text.Encoding]::Unicode)

function Replace-Row([string[]]$rows, [int]$index, [int]$led, [string]$value) {
    $parts = $rows[$index].Split('|')
    if ($parts.Count -ne 10) { throw "Row $index does not have 10 LEDs" }
    if ($led -eq -1) {
        for ($i = 0; $i -lt 10; $i++) { $parts[$i] = $value }
    } else {
        $parts[$led] = $value
    }
    return ($parts -join '|')
}

$selRows = (Get-Field $text 'Lighting1ColorSelected').TrimEnd(';').Split(';')
$colRows = (Get-Field $text 'Lighting1FixedColor').TrimEnd(';').Split(';')

$selRows[$Row] = Replace-Row $selRows $Row $Led '01'
$colRows[$Row] = Replace-Row $colRows $Row $Led $Color.ToUpperInvariant()

$text = Set-Field $text 'Lighting1ColorSelected' (($selRows -join ';') + ';')
$text = Set-Field $text 'Lighting1FixedColor' (($colRows -join ';') + ';')
$text = Set-Field $text 'Lighting1ModeValue' ("{0:X2}0000" -f $Mode)
$text = Set-Field $text 'Lighting1Name' $Name
$text = Set-Field $text 'Lighting1OffOn' '01'

if ($Speed -ge 0) {
    $v = (Get-Field $text 'Lighting1Speed').Split('|')
    $v[$Row] = "$Speed"
    $text = Set-Field $text 'Lighting1Speed' ($v -join '|')
}
if ($Brightness -ge 0) {
    $v = (Get-Field $text 'Lighting1Brightness').Split('|')
    $v[$Row] = "$Brightness"
    $text = Set-Field $text 'Lighting1Brightness' ($v -join '|')
}
if ($Direction -ge 0) {
    $v = (Get-Field $text 'Lighting1Direction').Split('|')
    $v[$Row] = "$Direction"
    $text = Set-Field $text 'Lighting1Direction' ($v -join '|')
}
if ($Logo) {
    $text = Set-Field $text 'Lighting2OffOn' '01'
    $text = Set-Field $text 'Lighting2FixedColor' $Color.ToUpperInvariant()
}

[IO.File]::WriteAllText($Profile, $text, [Text.Encoding]::Unicode)
Write-Host "Updated $Profile (backup: $backupPath)" -ForegroundColor Green
Show-Rows $text $Profile
