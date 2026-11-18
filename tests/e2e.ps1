# End-to-end test for MouseMackros.
# Replaces the live config with a tiny test profile, starts the app, emulates a
# Ctrl+Alt+F12 combo and an X1 side-button click, then checks that both bound
# actions actually ran. The original config is restored afterwards.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tests\e2e.ps1

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$exe     = Join-Path $root 'src\MouseMackros\bin\Debug\net10.0-windows\MouseMackros.exe'
$appData = Join-Path $env:APPDATA 'MouseMackros'
$config  = Join-Path $appData 'config.json'
$backup  = Join-Path $env:TEMP 'mm_config_e2e_backup.json'
$keyMarker   = Join-Path $env:TEMP 'mm_e2e_key.txt'
$mouseMarker = Join-Path $env:TEMP 'mm_e2e_mouse.txt'

if (-not (Test-Path $exe)) {
    throw "Не найден $exe — сначала соберите проект (dotnet build)."
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class E2EInput
{
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    public static void Key(byte vk, bool down)
    {
        keybd_event(vk, 0, down ? 0u : 2u, UIntPtr.Zero);
    }

    public static void XButton(bool down)
    {
        mouse_event(down ? 0x0080u : 0x0100u, 0, 0, 1, UIntPtr.Zero);
    }
}
'@

Get-Process MouseMackros -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$hadConfig = Test-Path $config
if ($hadConfig) { Copy-Item $config $backup -Force }

Remove-Item $keyMarker, $mouseMarker -Force -ErrorAction SilentlyContinue

$json = @'
{
  "Version": 1,
  "Enabled": true,
  "ShowToast": false,
  "StartWithWindows": false,
  "ToggleHotkey": "Ctrl+Alt+P",
  "DefaultProfileId": "default",
  "Profiles": [
    {
      "Id": "default",
      "Name": "E2E",
      "Accent": "#4C9AFF",
      "IsFallback": true,
      "Bindings": [
        {
          "Trigger": "key:Ctrl+Alt+Shift+F12",
          "Label": "key test",
          "Enabled": true,
          "Action": {
            "Kind": "Run",
            "Value": "cmd.exe",
            "Arguments": "/c echo keyok> \"__KEY__\""
          }
        },
        {
          "Trigger": "mouse:X1",
          "Label": "mouse test",
          "Enabled": true,
          "Action": {
            "Kind": "Run",
            "Value": "cmd.exe",
            "Arguments": "/c echo mouseok> \"__MOUSE__\""
          }
        }
      ]
    }
  ]
}
'@

$json = $json.Replace('__KEY__', $keyMarker.Replace('\', '\\')).Replace('__MOUSE__', $mouseMarker.Replace('\', '\\'))
New-Item -ItemType Directory -Force -Path $appData | Out-Null
[IO.File]::WriteAllText($config, $json, (New-Object Text.UTF8Encoding($false)))

function Send-Combo {
    [E2EInput]::Key(0x11, $true)   # Ctrl
    Start-Sleep -Milliseconds 40
    [E2EInput]::Key(0x12, $true)   # Alt
    Start-Sleep -Milliseconds 40
    [E2EInput]::Key(0x10, $true)   # Shift
    Start-Sleep -Milliseconds 40
    [E2EInput]::Key(0x7B, $true)   # F12
    Start-Sleep -Milliseconds 40
    [E2EInput]::Key(0x7B, $false)
    Start-Sleep -Milliseconds 40
    [E2EInput]::Key(0x10, $false)
    [E2EInput]::Key(0x12, $false)
    [E2EInput]::Key(0x11, $false)
}

try {
    Write-Host 'Запуск MouseMackros --silent ...'
    Start-Process -FilePath $exe -ArgumentList '--silent'
    Start-Sleep -Seconds 3

    $proc = Get-Process MouseMackros -ErrorAction SilentlyContinue
    if ($proc) { Write-Host ("Процесс работает, PID " + ($proc.Id -join ', ')) }
    else { Write-Host 'ПРОЦЕСС НЕ ЗАПУЩЕН' }

    Write-Host 'Отправка Ctrl+Alt+Shift+F12 ...'
    Send-Combo
    Start-Sleep -Milliseconds 500

    Write-Host 'Отправка боковой кнопки X1 ...'
    [E2EInput]::XButton($true)
    Start-Sleep -Milliseconds 60
    [E2EInput]::XButton($false)
    Start-Sleep -Seconds 2

    $keyOk = Test-Path $keyMarker
    $mouseOk = Test-Path $mouseMarker

    Write-Host ''
    Write-Host ("Клавиатурный триггер (Ctrl+Alt+Shift+F12): " + $(if ($keyOk) { 'OK' } else { 'FAIL' }))
    Write-Host ("Мышиный триггер (X1):              " + $(if ($mouseOk) { 'OK' } else { 'FAIL' }))
    if ($keyOk) { Write-Host ("  файл: " + (Get-Content $keyMarker -Raw).Trim()) }
    if ($mouseOk) { Write-Host ("  файл: " + (Get-Content $mouseMarker -Raw).Trim()) }
    Write-Host ''

    if ($keyOk -and $mouseOk) { exit 0 } else { exit 1 }
}
finally {
    Get-Process MouseMackros -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    if ($hadConfig) { Copy-Item $backup $config -Force; Remove-Item $backup -Force }
    else { Remove-Item $config -Force -ErrorAction SilentlyContinue }

    Remove-Item $keyMarker, $mouseMarker -Force -ErrorAction SilentlyContinue
    Write-Host 'Исходный конфиг восстановлен.'
}
