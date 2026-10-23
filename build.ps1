param(
    [switch]$TestBuild,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到 .NET Framework C# 编译器。请启用或安装 .NET Framework 4.8 桌面组件。'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $target = if ($TestBuild) {
        Join-Path $PSScriptRoot 'artifacts\tests\ReadingFocus.Tests.exe'
    } else {
        Join-Path $PSScriptRoot 'artifacts\文献专注.exe'
    }
} elseif ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $target = [System.IO.Path]::GetFullPath($OutputPath)
} else {
    $target = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputPath))
}
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null

$sourceRoot = Join-Path $PSScriptRoot 'src'
$icon = Join-Path $PSScriptRoot 'assets\FocusTimer.ico'
$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    '/langversion:5', '/codepage:65001', '/warn:4',
    ('/out:' + $target),
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),
    ('/win32icon:' + $icon),
    ('/resource:' + $icon + ',FocusTimer.ico'),
    ('/reference:' + (Join-Path $framework 'WPF\PresentationFramework.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\WindowsBase.dll')),
    ('/reference:' + (Join-Path $framework 'System.Xaml.dll')),
    '/reference:System.Core.dll', '/reference:System.Xml.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll'
)
$arguments += @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File |
    Sort-Object Name | ForEach-Object { $_.FullName })
if ($TestBuild) {
    $arguments += '/define:TESTING'
    $arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' -File |
        Sort-Object Name | ForEach-Object { $_.FullName })
}

& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw ('编译失败，退出码：' + $LASTEXITCODE) }
Write-Output $target
