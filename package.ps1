param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

& (Join-Path $PSScriptRoot 'build.ps1') | Out-Host
$releaseFiles = @(
    (Join-Path $PSScriptRoot 'artifacts\文献专注.exe'),
    (Join-Path $PSScriptRoot 'docs\使用说明.md'),
    (Join-Path $PSScriptRoot 'docs\RELEASE-1.2.md'),
    (Join-Path $PSScriptRoot 'LICENSE')
)
foreach ($file in $releaseFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('缺少发行文件：' + $file) }
}
$archive = Join-Path $PSScriptRoot 'artifacts\ReadingFocus-1.2-win.zip'
# 只打包明确列出的四个文件；不遍历应用数据、词表或笔记目录。
Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archive -CompressionLevel Optimal -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
    $expected = @('文献专注.exe', '使用说明.md', 'RELEASE-1.2.md', 'LICENSE') | Sort-Object
    $actual = @($zip.Entries | ForEach-Object { $_.FullName }) | Sort-Object
    if ($actual.Count -ne 4 -or @(Compare-Object $expected $actual).Count -ne 0) {
        throw '发行包内容校验失败：只能包含程序、使用说明、发行说明和许可证。'
    }
} finally {
    $zip.Dispose()
}
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts\ReadingFocus-1.2-SHA256.txt'), $hash.ToLowerInvariant() + '  ReadingFocus-1.2-win.zip' + "`r`n")
Write-Host ('SHA256: ' + $hash)
Write-Output $archive
