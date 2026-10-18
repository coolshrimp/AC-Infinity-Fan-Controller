$ErrorActionPreference = 'Stop'
[xml]$aciProject = Get-Content (Join-Path $PSScriptRoot 'ACInfinityDesktop.csproj')
$aciVersion = $aciProject.Project.PropertyGroup.Version
$aciExe = Join-Path $PSScriptRoot "Releases\dotnet-$aciVersion\AC-Infinity-Desktop.exe"
if (-not (Test-Path -LiteralPath $aciExe)) { throw 'Run publish.ps1 first.' }
$aciChecks = Join-Path $PSScriptRoot 'diagnostics\verification'
New-Item -ItemType Directory -Force -Path $aciChecks | Out-Null

function Invoke-Check([string]$Arguments, [string]$OutputName) {
    $output = Join-Path $aciChecks $OutputName
    foreach($previous in @($output, "$output.error.txt", "$output.layout.json")) {
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous }
    }
    $process = Start-Process -FilePath $aciExe -ArgumentList "$Arguments --output `"$output`"" -WindowStyle Hidden -PassThru
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
if ($aciResult.basePassed -lt 379 -or $aciResult.controllerEditingPassed -lt 1) {
    throw 'Offline protocol/scheduling or controller-editing checks are missing.'
}
$aciExpectedTotal = [int]$aciResult.basePassed + [int]$aciResult.controllerEditingPassed
if ($aciResult.passed -ne $aciExpectedTotal -or $aciResult.hardwareWrites -ne $false) {
    throw 'Offline check totals or hardware-write isolation are invalid.'
}
# Smoke checks use in-memory preferences and never connect to a controller.
$aciViews = @('status', 'chart', 'settings', 'picker', 'bluetooth', 'history', 'programs', 'controller-cycle', 'app-schedule', 'controller-auto', 'controller-timer-on', 'controller-timer-off')
foreach ($view in $aciViews) {
    Invoke-Check "--smoke --$view" "$view.png"
}
Invoke-Check '--smoke --multi-controller' 'multiple-controllers.png'
Write-Host "Passed $($aciResult.passed) offline checks and $($aciViews.Count + 1) window checks."
