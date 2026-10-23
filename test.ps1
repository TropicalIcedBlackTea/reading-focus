param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

& (Join-Path $PSScriptRoot 'build.ps1') -TestBuild | Out-Host
$testExe = Join-Path $PSScriptRoot 'artifacts\tests\ReadingFocus.Tests.exe'
$resultsRoot = Join-Path $PSScriptRoot 'artifacts\test-results'

function Invoke-IsolatedCheck {
    param([string]$Mode, [string]$FolderName, [string]$ReportName)

    $resultFolder = Join-Path $resultsRoot $FolderName
    New-Item -ItemType Directory -Path $resultFolder -Force | Out-Null
    $report = Join-Path $resultFolder $ReportName
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
    Write-Host ('正在运行 ' + $Mode + '，使用独立合成数据。')
    $process = Start-Process -FilePath $testExe -ArgumentList @($Mode, ('"' + $resultFolder + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) {
        Stop-Process -Id $process.Id -Force
        throw ($Mode + ' 超过 60 秒，已停止测试进程。')
    }
    $process.Refresh()
    if (-not (Test-Path -LiteralPath $report)) {
        throw ($Mode + ' 未生成报告，退出码：' + $process.ExitCode)
    }
    $reportText = (Get-Content -LiteralPath $report -Encoding UTF8 -Raw).Trim()
    Write-Host $reportText
    if ($process.ExitCode -ne 0 -or $reportText -notmatch '^PASS:') {
        throw ($Mode + ' 未通过，退出码：' + $process.ExitCode + '。报告：' + $report)
    }
}

Invoke-IsolatedCheck '--self-test' 'core' 'timer-test-results.txt'
Invoke-IsolatedCheck '--flow-test' 'flow' 'flow-test-results.txt'
Write-Host ('全部检查通过。报告和组件渲染位于：' + $resultsRoot)
