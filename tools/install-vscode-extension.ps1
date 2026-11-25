$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'vscode-mouse-macros'
if (-not (Test-Path $source)) {
    throw "Не найдена папка расширения: $source"
}

$extensions = Join-Path $env:USERPROFILE '.vscode\extensions'
$target = Join-Path $extensions 'mousemackros.mouse-macros-0.1.0'

if (Test-Path $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}

New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

Write-Host "Расширение установлено: $target"
Write-Host "Перезапустите VS Code. Файлы .macro откроются как MouseMackros Macro."
