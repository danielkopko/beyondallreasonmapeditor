<#
.SYNOPSIS
    Builds the BAR map editor solution and optionally runs the tests.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Test
    .\build.ps1 -Configuration Release -Test -Filter Smf
    .\build.ps1 -Test -SelfTest
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Test,
    # Substring filters passed to the test runner (e.g. -Filter Smf,Archive).
    [string[]]$Filter = @(),
    # Also drive the editor window, hidden, through a whole session (MapEditor --selftest); results in out\selftest.
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# Tests work on copies under testdata/; the original map archive is never touched.
$testData = Join-Path $root 'testdata'
$testMap = Join-Path $testData 'acidicquarry_5.17.sd7'
$sourceMap = Join-Path $root 'acidicquarry_5.17.sd7'
if (-not (Test-Path $testMap) -and (Test-Path $sourceMap)) {
    New-Item -ItemType Directory -Force $testData | Out-Null
    Copy-Item $sourceMap $testMap
    Write-Host "Copied test map to $testMap"
}

& dotnet build (Join-Path $root 'BarMapEditor.slnx') -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$failed = 0
if ($Test) {
    $runner = Join-Path $root "tests\MapCore.Tests\bin\$Configuration\net10.0\MapCore.Tests.exe"
    # 'powershell -File' passes "A,B" as one string, so split it here.
    $filters = @($Filter | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    & $runner @filters
    if ($LASTEXITCODE -ne 0) { $failed = $LASTEXITCODE }
}

if ($SelfTest) {
    # Reached only after a successful build (the script exits above otherwise), so a stale editor never starts.
    $editor = Join-Path $root "src\MapEditor\bin\$Configuration\net10.0-windows\MapEditor.exe"
    $outDir = Join-Path $root 'out\selftest'
    $process = Start-Process $editor -ArgumentList '--selftest', "`"$testMap`"", "`"$outDir`"" -Wait -PassThru
    $report = Join-Path $outDir 'selftest.txt'
    $lines = @(if (Test-Path $report) { Get-Content $report -Encoding UTF8 | Where-Object { $_ -match '^(PASS|FAIL)' } })
    $lines | ForEach-Object { Write-Host $_ }
    $passed = @($lines | Where-Object { $_ -match '^PASS' }).Count
    Write-Host "`nEditor self-test: $passed/$($lines.Count) passed (exit code $($process.ExitCode)); details in $report"
    if ($process.ExitCode -ne 0) { $failed = $process.ExitCode }
}
exit $failed
