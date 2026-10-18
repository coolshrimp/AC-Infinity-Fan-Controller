$ErrorActionPreference = 'Stop'
[xml]$aciProject = Get-Content (Join-Path $PSScriptRoot 'ACInfinityDesktop.csproj')
$aciVersion = $aciProject.Project.PropertyGroup.Version
$aciExe = Join-Path $PSScriptRoot "Releases\dotnet-$aciVersion\AC-Infinity-Desktop.exe"
if (-not (Test-Path -LiteralPath $aciExe)) { throw 'Run publish.ps1 first.' }
$aciChecks = Join-Path $PSScriptRoot 'diagnostics\verification'
New-Item -ItemType Directory -Force -Path $aciChecks | Out-Null

function Invoke-Check([string]$Arguments, [string]$OutputName) {
    $output = Join-Path $aciChecks $OutputName
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
    $process = Start-Process -FilePath $aciExe -ArgumentList "$Arguments --output `"$output`"" -PassThru
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw "Check timed out: $Arguments"
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output)) {
        foreach($detail in Get-ChildItem -LiteralPath $aciChecks -Filter "$OutputName.*") {
            Write-Host $detail.Name
            Get-Content -LiteralPath $detail.FullName | Write-Host
        }
        throw "Check failed: $Arguments (exit code $($process.ExitCode))"
    }
}

Invoke-Check '--self-test' 'checks.json'
$aciResult = Get-Content (Join-Path $aciChecks 'checks.json') -Raw | ConvertFrom-Json
if ($aciResult.passed -lt 81) { throw 'Offline check count is below the expected baseline.' }
# Smoke checks use in-memory preferences and never connect to a controller.
foreach ($view in @('status', 'chart', 'settings', 'picker', 'bluetooth', 'history')) {
    Invoke-Check "--smoke --$view" "$view.png"
}
Invoke-Check '--smoke --multi-controller' 'multiple-controllers.png'
Write-Host "Passed $($aciResult.passed) offline checks and seven window checks."
